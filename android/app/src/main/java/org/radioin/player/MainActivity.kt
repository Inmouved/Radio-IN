package org.radioin.player

import android.animation.ArgbEvaluator
import android.animation.ValueAnimator
import android.app.Activity
import android.app.AlertDialog
import android.content.ComponentName
import android.content.Intent
import android.graphics.Bitmap
import android.graphics.Color
import android.graphics.Typeface
import android.graphics.drawable.GradientDrawable
import android.net.Uri
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.os.SystemClock
import android.text.Editable
import android.text.TextWatcher
import android.text.TextUtils
import android.view.Gravity
import android.view.View
import android.view.ViewGroup
import android.view.WindowManager
import android.widget.*
import androidx.media3.common.Player
import androidx.media3.session.MediaController
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionToken
import com.google.common.util.concurrent.ListenableFuture
import java.io.File
import java.text.SimpleDateFormat
import java.util.Date
import java.util.Locale

class MainActivity : Activity() {
    private val app get() = application as RadioApp
    private val store get() = app.store
    private val repo get() = app.repo
    private val main = Handler(Looper.getMainLooper())
    private var future: ListenableFuture<MediaController>? = null
    private var controller: MediaController? = null
    private lateinit var root: FrameLayout
    private lateinit var backdrop: ImageView
    private lateinit var content: LinearLayout
    private lateinit var titleView: TextView
    private lateinit var artistView: TextView
    private lateinit var statusView: TextView
    private lateinit var play: Button
    private lateinit var favorite: Button
    private lateinit var bars: Bars
    private lateinit var cover: ImageView
    private lateinit var detail: LinearLayout
    private var colorAnimator: ValueAnimator? = null
    private var tint = Color.rgb(22, 32, 42)
    private var autoTint = Color.rgb(40, 53, 39)
    private var art = ""
    private var artGeneration = 0
    private var tab = ""
    private var query = ""
    private var page = 1
    private var pages = 1
    private var catalogue: List<Track> = emptyList()
    private var catalogueGeneration = 0
    private var search: EditText? = null
    private var rows: LinearLayout? = null
    private var pager: TextView? = null
    private var previous: Button? = null
    private var next: Button? = null
    private var feedback = ""
    private var feedbackView: TextView? = null
    private var sending = false
    private var fetching = false
    private var queueFetching = false
    private var lastQueueRefresh = 0L
    private var queueTracks: List<RadioRepository.QueuedTrack> = emptyList()
    private var queueStatus = "Chargement de la file…"
    private var lastCatalogueRefresh = 0L
    private var active = false
    private val refreshListener: () -> Unit = { renderMetadata() }
    private val searchTask = Runnable { page = 1; loadCatalogue() }
    private val uiTimer = object : Runnable {
        override fun run() {
            if (!active) return
            updatePlayback()
            if (tab == "requests" && !fetching && !sending && SystemClock.elapsedRealtime() - lastCatalogueRefresh > 15000) loadCatalogue(silent = true)
            if (tab == "queue" && !queueFetching && SystemClock.elapsedRealtime() - lastQueueRefresh > 15000) loadQueue()
            main.postDelayed(this, 1000)
        }
    }
    private val playerListener = object : Player.Listener {
        override fun onEvents(player: Player, events: Player.Events) { updatePlayback() }
    }
    private fun dp(v: Int) = (v * resources.displayMetrics.density).toInt()
    private fun label(value: String, size: Float = 16f, color: Int = Color.WHITE, bold: Boolean = false) = TextView(this).apply {
        text = value; textSize = size; setTextColor(color); if (bold) setTypeface(typeface, Typeface.BOLD)
        setPadding(0, dp(3), 0, dp(3))
    }
    private fun column() = LinearLayout(this).apply { orientation = LinearLayout.VERTICAL }
    private fun row() = LinearLayout(this).apply { orientation = LinearLayout.HORIZONTAL; gravity = Gravity.CENTER_VERTICAL }
    private fun button(text: String, action: () -> Unit) = Button(this).apply {
        this.text = text; isAllCaps = false; textSize = 14f; minHeight = dp(48); minimumHeight = dp(48); minWidth = dp(48); minimumWidth = dp(48)
        setTextColor(Color.WHITE); background = rounded(Color.rgb(39, 53, 66), 14); setPadding(dp(12), dp(4), dp(12), dp(4))
        setOnClickListener { action() }
        layoutParams = LinearLayout.LayoutParams(ViewGroup.LayoutParams.WRAP_CONTENT, dp(48)).apply { setMargins(dp(3), dp(4), dp(3), dp(4)) }
    }
    private fun rounded(color: Int, radius: Int) = GradientDrawable().apply { setColor(color); cornerRadius = dp(radius).toFloat() }
    private fun gap(size: Int) = View(this).apply { layoutParams = LinearLayout.LayoutParams(1, dp(size)) }
    private fun marquee(v: TextView) { v.setSingleLine(); v.ellipsize = TextUtils.TruncateAt.MARQUEE; v.marqueeRepeatLimit = -1; v.isSelected = true }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        volumeControlStream = android.media.AudioManager.STREAM_MUSIC
        tab = savedInstanceState?.getString("tab", "") ?: ""
        if (tab !in setOf("history", "requests", "queue", "session", "favorites")) tab = ""
        query = savedInstanceState?.getString("query", "") ?: ""
        page = savedInstanceState?.getInt("page", 1) ?: 1
        store.mode = 0; store.theme = 5
        build()
        val token = SessionToken(this, ComponentName(this, PlaybackService::class.java))
        future = MediaController.Builder(this, token).buildAsync().also { f ->
            f.addListener({
                if (isDestroyed) return@addListener
                try { controller = f.get(); controller?.addListener(playerListener); controller?.volume = store.volume; updatePlayback(); if (savedInstanceState == null && store.autoPlay) startAudio() }
                catch (e: Exception) { repo.log("Connexion au lecteur", e); statusView.text = "Lecteur indisponible · réouvrez l’application" }
            }, { task -> runOnUiThread(task) })
        }
    }
    override fun onStart() { super.onStart(); active = true; repo.listeners.add(refreshListener); repo.refresh(); renderMetadata(); checkForUpdate(false); main.post(uiTimer) }
    override fun onStop() { active = false; bars.running = false; main.removeCallbacks(uiTimer); repo.listeners.remove(refreshListener); super.onStop() }
    override fun onDestroy() { main.removeCallbacksAndMessages(null); colorAnimator?.cancel(); controller?.removeListener(playerListener); future?.let { MediaController.releaseFuture(it) }; super.onDestroy() }
    override fun onSaveInstanceState(outState: Bundle) { outState.putString("tab", tab); outState.putString("query", query); outState.putInt("page", page); super.onSaveInstanceState(outState) }

    private fun build() {
        colorAnimator?.cancel(); artGeneration++; art = ""; store.mode = 0; store.theme = 5
        if (store.keepScreen) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        root = FrameLayout(this).apply { setBackgroundColor(tint) }
        backdrop = ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_CROP; alpha = .18f; importantForAccessibility = View.IMPORTANT_FOR_ACCESSIBILITY_NO }
        root.addView(backdrop, FrameLayout.LayoutParams(-1, -1))
        // Target 36 enforces edge-to-edge: apply actual system/keyboard insets on every version.
        root.setOnApplyWindowInsetsListener { v, insets ->
            if (android.os.Build.VERSION.SDK_INT >= 30) { val i = insets.getInsets(android.view.WindowInsets.Type.systemBars() or android.view.WindowInsets.Type.displayCutout() or android.view.WindowInsets.Type.ime()); v.setPadding(i.left, i.top, i.right, i.bottom) }
            else { @Suppress("DEPRECATION") v.setPadding(insets.systemWindowInsetLeft, insets.systemWindowInsetTop, insets.systemWindowInsetRight, insets.systemWindowInsetBottom) }
            insets
        }
        content = column().apply { setPadding(dp(20), dp(14), dp(20), dp(170)) }
        val scroll = ScrollView(this).apply { isFillViewport = true; isVerticalScrollBarEnabled = false; addView(content) }
        root.addView(scroll, FrameLayout.LayoutParams(-1, -1)); setContentView(root); root.requestApplyInsets()
        val header = row()
        header.addView(ImageView(this).apply { setImageResource(R.drawable.logo); contentDescription = "Logo Radio IN" }, LinearLayout.LayoutParams(dp(36), dp(36)))
        header.addView(column().apply { setPadding(dp(10), 0, 0, 0); addView(label("RADIO IN", 19f, bold = true)); addView(label("BÊTA 1.1 ANDROID", 10f, Color.rgb(180, 195, 202))) }, LinearLayout.LayoutParams(0, -2, 1f))
        header.addView(button("⋯") { options() }.apply { contentDescription = "Options, favoris et minuterie" }); content.addView(header)
        content.addView(gap(8))
        val live = row(); bars = Bars(this); live.addView(bars, LinearLayout.LayoutParams(dp(24), dp(22))); live.addView(label("  EN DIRECT", 12f, Color.rgb(199, 237, 133), true)); content.addView(live)
        cover = ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_INSIDE; setImageResource(R.drawable.logo); contentDescription = "Pochette du titre diffusé"; background = rounded(Color.rgb(25, 36, 44), 20); clipToOutline = true }
        val width = (resources.displayMetrics.widthPixels / resources.displayMetrics.density).toInt() - 40
        val coverSize = minOf(width, 310)
        titleView = label(repo.current.title, 26f, bold = true); marquee(titleView)
        artistView = label(repo.current.artist, 17f, Color.rgb(202, 214, 220)); marquee(artistView)
        statusView = label("Prêt à écouter", 12f, Color.rgb(181, 197, 204))
        play = button("▶  Écouter") { toggleAudio() }.apply { textSize = 17f; setTextColor(Color.rgb(17, 30, 24)); background = rounded(Color.rgb(199, 237, 133), 16) }
        favorite = button("♡") { toggleFavorite() }.apply { contentDescription = "Ajouter ou retirer ce titre des favoris"; textSize = 24f }
        val audioRow = row(); audioRow.addView(play, LinearLayout.LayoutParams(0, dp(56), 1f)); audioRow.addView(favorite, LinearLayout.LayoutParams(dp(56), dp(56)).apply { marginStart = dp(8) })
        val volumeRow = row(); volumeRow.addView(label("Volume", 12f))
        val volume = SeekBar(this).apply {
            max = 100; progress = (store.volume * 100).toInt(); contentDescription = "Volume de Radio IN"
            setOnSeekBarChangeListener(object : SeekBar.OnSeekBarChangeListener {
                override fun onProgressChanged(seek: SeekBar, value: Int, fromUser: Boolean) { if (fromUser) { store.volume = value / 100f; controller?.volume = store.volume; updatePlayback() } }
                override fun onStartTrackingTouch(seek: SeekBar) { }
                override fun onStopTrackingTouch(seek: SeekBar) { }
            })
        }; volumeRow.addView(volume, LinearLayout.LayoutParams(0, dp(48), 1f))
        val trackLine = row(); trackLine.addView(cover, LinearLayout.LayoutParams(dp(minOf(coverSize, 80)), dp(minOf(coverSize, 80))))
        val metadata = column().apply { setPadding(dp(10), 0, 0, 0); addView(titleView); addView(artistView); addView(statusView) }
        trackLine.addView(metadata, LinearLayout.LayoutParams(0, -2, 1f)); content.addView(trackLine)
        detail = column(); search = null; rows = null; feedbackView = null
        val navigation = row()
        navigation.addView(button("Historique") { openTab("history") }, LinearLayout.LayoutParams(0, dp(48), 1f).apply { marginEnd = dp(3) })
        navigation.addView(button("À venir") { openTab("queue") }, LinearLayout.LayoutParams(0, dp(48), 1f).apply { setMargins(dp(3), 0, dp(3), 0) })
        navigation.addView(button("Demander") { openTab("requests") }, LinearLayout.LayoutParams(0, dp(48), 1f).apply { marginStart = dp(3) })
        content.addView(navigation)
        if (tab.isBlank()) tab = "history"
        renderTab(); content.addView(detail)
        val dock = column().apply { setPadding(dp(16), dp(10), dp(16), dp(12)); background = GradientDrawable(GradientDrawable.Orientation.TOP_BOTTOM, intArrayOf(0x0016202A, 0xF016202A.toInt())); addView(audioRow); addView(volumeRow); addView(label("Vous écoutez Radio IN en direct", 11f, Color.rgb(167, 184, 193))) }
        root.addView(dock, FrameLayout.LayoutParams(-1, -2, Gravity.BOTTOM))
        applyTheme(false); renderMetadata(); updatePlayback()
    }
    private fun openTab(value: String) {
        if (tab != value) page = 1
        tab = value; renderTab()
    }
    private fun renderTab() {
        if (!::detail.isInitialized) return
        detail.removeAllViews(); search = null; rows = null; feedbackView = null
        if (tab.isBlank()) return
        detail.addView(gap(4))
        val heading = row(); heading.addView(label(when (tab) { "history" -> "Derniers titres"; "favorites" -> "Mes favoris"; "queue" -> "À venir"; "session" -> "Ma session"; else -> "Demander un titre" }, 18f, bold = true), LinearLayout.LayoutParams(0, -2, 1f)); detail.addView(heading)
        when (tab) { "history" -> historyRows(detail); "favorites" -> favoritesPane(); "requests" -> requestPane(detail); "queue" -> queuePane(); "session" -> sessionPane() }
    }
    private fun trackRow(track: Track, action: (() -> Unit)? = null, remove: (() -> Unit)? = null): View {
        val outer = row().apply { setPadding(dp(12), dp(8), dp(8), dp(8)); background = rounded(0x60202C39, 12) }
        val text = column(); text.addView(label(track.title, 15f, bold = true)); text.addView(label((if (track.time > 0) SimpleDateFormat("HH:mm", Locale.FRANCE).format(Date(track.time * 1000)) + " · " else "") + track.artist, 12f, Color.rgb(192, 208, 218))); outer.addView(text, LinearLayout.LayoutParams(0, -2, 1f))
        outer.addView(button(if (store.favorites().any { it.id == track.id }) "♥" else "♡") { toggleTrackFavorite(track) }.apply { contentDescription = "Ajouter ${track.title} aux favoris" })
        if (action != null) outer.addView(button("＋") { action() }.apply { contentDescription = "Demander ${track.title}"; isEnabled = !sending })
        if (remove != null) outer.addView(button("×") { remove() }.apply { textSize = 24f; contentDescription = "Retirer ${track.title} des favoris" })
        outer.layoutParams = LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(5) }; return outer
    }
    private fun historyRows(target: LinearLayout) {
        if (repo.history.isEmpty()) target.addView(label("Historique en cours de chargement…", 13f, Color.LTGRAY))
        repo.history.take(5).forEach { t -> target.addView(trackRow(t)) }
        target.addView(button("Historique de cette session") { tab = "session"; renderTab() })
    }
    private fun toggleTrackFavorite(track: Track) {
        if (store.favorites().any { it.id == track.id }) store.remove(track.id) else store.add(track)
        renderTab(); updateFavorite()
    }
    private fun sessionPane() {
        val tracks = repo.sessionHistory.toList()
        if (tracks.isEmpty()) detail.addView(label("Les titres entendus apparaîtront ici pendant l’écoute.", 13f, Color.LTGRAY))
        tracks.take(20).forEach { detail.addView(trackRow(it)) }
    }
    private fun queuePane() {
        detail.addView(label(queueStatus, 12f, Color.rgb(192, 208, 218)))
        if (queueTracks.isEmpty()) detail.addView(button("Actualiser") { loadQueue() })
        queueTracks.take(5).forEach { queued ->
            val row = row().apply { setPadding(dp(10), dp(5), dp(8), dp(5)); background = rounded(0x60202C39, 12) }
            val art = ImageView(this).apply { scaleType = ImageView.ScaleType.CENTER_CROP; setImageResource(R.drawable.logo); contentDescription = "Pochette de ${queued.track.title}" }
            row.addView(art, LinearLayout.LayoutParams(dp(44), dp(44)).apply { marginEnd = dp(9) })
            if (queued.track.art.startsWith("https://radio-in.duckdns.org/")) app.artwork.load(queued.track.art) { bitmap -> if (!isDestroyed && bitmap != null) art.setImageBitmap(bitmap) }
            val eta = if (queued.expected > 0) {
                val remaining = (queued.expected - System.currentTimeMillis() / 1000).coerceAtLeast(0)
                SimpleDateFormat("HH:mm", Locale.FRANCE).format(Date(queued.expected * 1000)) + " · dans " + if (remaining >= 3600) "${remaining / 3600} h ${(remaining / 60) % 60} min" else "${remaining / 60} min"
            } else "horaire à confirmer"
            val info = column(); info.addView(label(queued.track.title, 14f, bold = true)); info.addView(label("${queued.track.artist} · $eta", 11f, Color.LTGRAY)); info.addView(label(if (queued.requested) "Demande auditeur" else "À venir", 10f, Color.rgb(199, 237, 133)))
            row.addView(info, LinearLayout.LayoutParams(0, -2, 1f)); detail.addView(row, LinearLayout.LayoutParams(-1, -2).apply { topMargin = dp(4) })
        }
    }
    private fun loadQueue() {
        if (queueFetching) return
        queueFetching = true; lastQueueRefresh = SystemClock.elapsedRealtime()
        repo.upcoming { result -> queueFetching = false; result.fold({ snapshot ->
            queueTracks = snapshot.tracks
            queueStatus = if (snapshot.stale) "File ancienne · horaires à confirmer" else "File actualisée · horaires estimatifs"
        }, { e -> queueStatus = if (queueTracks.isEmpty()) "File momentanément indisponible" else "Connexion interrompue · dernière file conservée"; repo.log("File à venir", e) })
            if (!isDestroyed && tab == "queue") renderTab()
        }
    }
    private fun favoritesPane() {
        val favorites = store.favorites()
        if (favorites.isEmpty()) { detail.addView(label("Ajoutez le titre en cours avec le cœur ♡.", 14f)); return }
        val favoritePage = page.coerceIn(1, ((favorites.size + 19) / 20).coerceAtLeast(1)); page = favoritePage
        favorites.drop((page - 1) * 20).take(20).forEach { t -> detail.addView(trackRow(t, { findFavorite(t) }, { store.remove(t.id); renderTab(); updateFavorite() })) }
        val buttons = row(); buttons.addView(button("‹") { page--; renderTab() }.apply { isEnabled = page > 1 }); buttons.addView(label("$page / ${(favorites.size + 19) / 20}")); buttons.addView(button("›") { page++; renderTab() }.apply { isEnabled = page * 20 < favorites.size }); detail.addView(buttons)
    }
    private fun requestPane(target: LinearLayout) {
        search = EditText(this).apply {
            hint = "Rechercher un titre ou un artiste"; setTextColor(Color.WHITE); setHintTextColor(Color.rgb(179, 194, 203)); textSize = 15f; isSingleLine = true; setText(query); setPadding(dp(12), dp(6), dp(12), dp(6)); background = rounded(0x90334150.toInt(), 12)
            addTextChangedListener(object : TextWatcher {
                override fun beforeTextChanged(s: CharSequence?, start: Int, count: Int, after: Int) { }
                override fun onTextChanged(s: CharSequence?, start: Int, before: Int, count: Int) { query = s.toString(); catalogueGeneration++; main.removeCallbacks(searchTask); main.postDelayed(searchTask, 450) }
                override fun afterTextChanged(s: Editable?) { }
            })
        }
        target.addView(search, LinearLayout.LayoutParams(-1, dp(52)))
        feedbackView = label(feedback, 13f, Color.rgb(199, 237, 133)); target.addView(feedbackView)
        rows = column(); target.addView(rows)
        val pagination = row()
        previous = button("‹") { if (page > 1) { page--; loadCatalogue() } }.apply { contentDescription = "Page précédente" }
        pager = label("$page / $pages", 13f)
        next = button("›") { if (page < pages) { page++; loadCatalogue() } }.apply { contentDescription = "Page suivante" }
        pagination.addView(previous); pagination.addView(pager); pagination.addView(next); target.addView(pagination); loadCatalogue()
    }
    private fun loadCatalogue(silent: Boolean = false) {
        val generation = ++catalogueGeneration; val requestedQuery = query; val requestedPage = page; fetching = true; lastCatalogueRefresh = SystemClock.elapsedRealtime()
        if (!silent) { rows?.removeAllViews(); rows?.addView(label("Chargement du catalogue…", 14f)) }
        repo.catalogue(requestedQuery, requestedPage) { result ->
            if (isDestroyed || generation != catalogueGeneration) { if (generation == catalogueGeneration - 1) fetching = false; return@catalogue }
            fetching = false
            result.fold({ data ->
                catalogue = data.tracks; pages = data.pages; rows?.removeAllViews()
                if (data.tracks.isEmpty()) rows?.addView(label("Aucun titre trouvé.", 14f))
                data.tracks.forEach { t -> rows?.addView(trackRow(t, { confirmRequest(t) })) }
                pager?.text = "$page / $pages"; previous?.isEnabled = page > 1; next?.isEnabled = page < pages
            }, { error -> if (!silent) { rows?.removeAllViews(); rows?.addView(label("Catalogue indisponible. Touchez pour réessayer.", 14f).apply { setOnClickListener { loadCatalogue() } }) }; repo.log("Catalogue", error) })
        }
    }
    private fun confirmRequest(track: Track) {
        if (sending) return
        if (!repo.requestsEnabled) { message("Les demandes de titres sont actuellement désactivées par la radio."); return }
        if (System.currentTimeMillis() < repo.cooldownUntil) { message("Patientez encore ${(repo.cooldownUntil - System.currentTimeMillis()) / 1000 + 1} secondes."); return }
        AlertDialog.Builder(this).setTitle("Demander ce titre ?").setMessage("${track.title}\n${track.artist}").setNegativeButton("Annuler", null).setPositiveButton("Demander") { _, _ ->
            sending = true; feedback = "Envoi de la demande…"; feedbackView?.text = feedback
            repo.request(track) { result ->
                sending = false
                result.fold({ r -> feedback = (if (r.accepted) "✓ Demande acceptée. " else "") + r.message + if (r.retrySeconds > 0) " Nouvelle demande possible dans ${r.retrySeconds}s." else "" }, { e -> feedback = "La demande n’a pas pu être confirmée. Vérifiez votre connexion avant de réessayer."; repo.log("Demande de titre", e) })
                if (!isDestroyed) { feedbackView?.text = feedback; message(feedback) }
            }
        }.show()
    }
    private fun findFavorite(track: Track, p: Int = 1) {
        if (p == 1) { if (sending) return; sending = true; Toast.makeText(this, "Recherche dans le catalogue…", Toast.LENGTH_SHORT).show() }
        repo.catalogue(track.title, p) { result ->
            if (isDestroyed) { sending = false; return@catalogue }
            result.fold({ c -> val found = c.tracks.firstOrNull { it.id == track.id }
                if (found != null) { sending = false; confirmRequest(found) }
                else if (p < c.pages && p < 50) findFavorite(track, p + 1)
                else { sending = false; message("Ce favori n’est pas disponible dans le catalogue des demandes.") }
            }, { sending = false; message("Catalogue indisponible. Réessayez plus tard.") })
        }
    }
    private fun renderMetadata() {
        if (!::titleView.isInitialized) return
        val t = repo.current
        if (titleView.text.toString() != t.title) titleView.text = t.title
        if (artistView.text.toString() != t.artist) artistView.text = t.artist
        updateFavorite(); updatePlayback()
        if (art != t.art) { art = t.art; val generation = ++artGeneration
            app.artwork.load(art) { bitmap ->
                if (isDestroyed || generation != artGeneration) return@load
                cover.animate().cancel()
                cover.scaleType = if (bitmap == null) ImageView.ScaleType.CENTER_INSIDE else ImageView.ScaleType.CENTER_CROP
                val old = cover.drawable
                val oldDrawable = if (old is android.graphics.drawable.TransitionDrawable) old.getDrawable(old.numberOfLayers - 1) else old
                val newDrawable = if (bitmap != null) android.graphics.drawable.BitmapDrawable(resources, bitmap) else getDrawable(R.drawable.logo)!!
                if (oldDrawable == null) cover.setImageDrawable(newDrawable) else cover.setImageDrawable(android.graphics.drawable.TransitionDrawable(arrayOf(oldDrawable, newDrawable)).apply { isCrossFadeEnabled = true; startTransition(700) })
                backdrop.setImageDrawable(newDrawable.constantState?.newDrawable(resources) ?: newDrawable)
                if (bitmap != null) { autoTint = artworkTint(bitmap); if (store.theme == 5 || store.theme == 0) applyTheme(true) }
            }
        }
        if (tab == "history" || tab == "session" || tab == "queue") renderTab()
    }
    private fun updateFavorite() { if (::favorite.isInitialized) favorite.text = if (store.favorites().any { it.id == repo.current.id }) "♥" else "♡" }
    private fun toggleFavorite() { val t = repo.current; if (t.id.isBlank()) return; if (store.favorites().any { it.id == t.id }) store.remove(t.id) else store.add(t); updateFavorite(); if (tab == "favorites") renderTab() }
    private fun startAudio() { val p = controller ?: return; if (p.playbackState == Player.STATE_IDLE) p.prepare(); p.play() }
    private fun toggleAudio() { val p = controller ?: return; if (p.playWhenReady) { p.pause(); p.stop() } else startAudio(); updatePlayback() }
    private fun updatePlayback() {
        if (!::play.isInitialized) return
        val p = controller
        val playing = p?.isPlaying == true; play.isEnabled = p != null
        play.text = if (p?.playWhenReady == true) "■  Arrêter" else "▶  Écouter"
        bars.running = active && playing && (p?.volume ?: 0f) > 0f && getSystemService(android.media.AudioManager::class.java).getStreamVolume(android.media.AudioManager.STREAM_MUSIC) > 0
        val timer = if (PlaybackService.sleepDeadline > SystemClock.elapsedRealtime()) " · veille dans ${(PlaybackService.sleepDeadline - SystemClock.elapsedRealtime() + 59999) / 60000} min" else ""
        statusView.text = (if (playing) "En direct" else PlaybackService.connection) + timer + if (!repo.online) " · radio hors ligne" else if (repo.status.startsWith("Informations indisponibles")) " · informations indisponibles" else ""
    }
    private fun artworkTint(b: Bitmap): Int { var r = 0; var g = 0; var blue = 0; var n = 0
        for (x in 0 until b.width step (b.width / 12).coerceAtLeast(1)) for (y in 0 until b.height step (b.height / 12).coerceAtLeast(1)) { val c = b.getPixel(x, y); r += Color.red(c); g += Color.green(c); blue += Color.blue(c); n++ }
        return Color.rgb((r / n * .22).toInt() + 9, (g / n * .22).toInt() + 12, (blue / n * .22).toInt() + 14)
    }
    private fun applyTheme(animate: Boolean) {
        backdrop.visibility = if (store.theme == 0) View.VISIBLE else View.GONE
        val value = when (store.theme) { 1 -> Color.rgb(14, 21, 30); 2 -> Color.rgb(66, 28, 22); 3 -> Color.rgb(21, 49, 36); 4 -> Color.rgb(19, 38, 65); else -> autoTint }
        colorAnimator?.cancel()
        if (animate) colorAnimator = ValueAnimator.ofObject(ArgbEvaluator(), tint, value).apply { duration = 700; addUpdateListener { tint = it.animatedValue as Int; root.setBackgroundColor(tint) }; start() }
        else { tint = value; root.setBackgroundColor(tint) }
    }
    private fun options() {
        val choices = arrayOf("Mes favoris", "Rechercher une mise à jour", "Minuterie de sommeil", "Partager le titre diffusé", "Exporter favoris et réglages", "Importer une sauvegarde", "Préférences", "À propos et diagnostic", "Quitter et arrêter le son")
        AlertDialog.Builder(this).setTitle("Radio IN").setItems(choices) { _, i -> when (i) {
            0 -> showFavorites(); 1 -> checkForUpdate(true); 2 -> sleep()
            3 -> startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, "J’écoute ${repo.current.artist} — ${repo.current.title} sur Radio IN 📻\n${RadioRepository.ORIGIN}/public/radio_in"), "Partager le titre"))
            4 -> startActivityForResult(Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json").putExtra(Intent.EXTRA_TITLE, "RadioIN-sauvegarde.json"), 10)
            5 -> startActivityForResult(Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("*/*").putExtra(Intent.EXTRA_MIME_TYPES, arrayOf("application/json", "text/plain", "application/octet-stream")), 11)
            6 -> preferences(); 7 -> about(); 8 -> { controller?.pause(); controller?.stop(); finishAndRemoveTask() }
        } }.show()
    }
    private fun showFavorites() {
        val favorites = store.favorites()
        if (favorites.isEmpty()) { message("Ajoutez le titre en cours avec le cœur ♡."); return }
        AlertDialog.Builder(this).setTitle("Mes favoris · ${favorites.size}").setItems(favorites.map { "${it.title}\n${it.artist}" }.toTypedArray()) { _, index ->
            val track = favorites[index]
            AlertDialog.Builder(this).setTitle(track.title).setMessage(track.artist).setNegativeButton("Fermer", null).setNeutralButton("Retirer") { _, _ -> store.remove(track.id); updateFavorite(); showFavorites() }.setPositiveButton("Demander ce titre") { _, _ -> findFavorite(track) }.show()
        }.setNegativeButton("Fermer", null).show()
    }
    private fun sleep() { val options = arrayOf("Annuler la minuterie", "15 minutes", "30 minutes", "60 minutes", "90 minutes", "120 minutes"); val values = intArrayOf(0, 15, 30, 60, 90, 120)
        AlertDialog.Builder(this).setTitle("Minuterie de sommeil").setItems(options) { _, which -> controller?.sendCustomCommand(SessionCommand(PlaybackService.SLEEP, Bundle.EMPTY), Bundle().apply { putInt("minutes", values[which]) }); main.postDelayed({ updatePlayback() }, 200) }.show()
    }
    private fun preferences() {
        AlertDialog.Builder(this).setTitle("Préférences").setMultiChoiceItems(arrayOf("Écouter automatiquement à l’ouverture", "Notifier les changements de titre", "Garder l’écran allumé dans le lecteur"), booleanArrayOf(store.autoPlay, store.notifications, store.keepScreen)) { _, which, checked -> when (which) {
            0 -> store.autoPlay = checked
            1 -> { store.notifications = checked; if (checked && android.os.Build.VERSION.SDK_INT >= 33 && checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) != android.content.pm.PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(android.Manifest.permission.POST_NOTIFICATIONS), 20) }
            2 -> { store.keepScreen = checked; if (checked) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON) }
        } }.setPositiveButton("Fermer", null).show()
    }
    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) { super.onRequestPermissionsResult(requestCode, permissions, grantResults); if (requestCode == 20 && grantResults.firstOrNull() != android.content.pm.PackageManager.PERMISSION_GRANTED) { store.notifications = false; Toast.makeText(this, "Notifications de titres désactivées. Le lecteur reste utilisable.", Toast.LENGTH_LONG).show() } }
    @Deprecated("Platform document picker result is intentionally supported back to Android 8")
    override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {
        super.onActivityResult(requestCode, resultCode, data); val uri = data?.data ?: return; if (resultCode != RESULT_OK) return
        if (requestCode == 10) { val text = store.export(); repo.io.execute { val result = runCatching { contentResolver.openOutputStream(uri, "wt")!!.use { it.write(text.toByteArray()) } }; main.post { if (!isDestroyed) message(if (result.isSuccess) "Sauvegarde exportée." else "Impossible d’écrire la sauvegarde.") } } }
        if (requestCode == 11) { repo.io.execute { val result = runCatching { contentResolver.openInputStream(uri)!!.use { it.readLimited(4 * 1024 * 1024) }.toString(Charsets.UTF_8) }; main.post { if (isDestroyed) return@post
            result.fold({ text -> AlertDialog.Builder(this).setTitle("Importer cette sauvegarde ?").setMessage("Les favoris seront fusionnés sans supprimer les favoris actuels. Le volume sera importé. Cette version reste en affichage compact avec le design automatique.").setNegativeButton("Annuler", null).setPositiveButton("Importer") { _, _ -> try { val added = store.importBackup(text); controller?.volume = store.volume; tab = ""; build(); message("Import terminé : $added nouveau(x) favori(s).") } catch (e: Exception) { message("Sauvegarde refusée : ${e.message}") } }.show() }, { message("Impossible de lire ce fichier (4 Mo maximum).") })
        } } }
    }
    private fun about() {
        val log = File(filesDir, "logs/RadioIN.log")
        AlertDialog.Builder(this).setTitle("Radio IN · Bêta 1.1 Android").setMessage("Lecteur natif Android 8 et versions suivantes.\n\n${statusView.text}\n${repo.status}\n\nVos favoris et réglages restent sur ce téléphone. Aucun compte ni outil publicitaire. Les journaux restent locaux ; leur export est facultatif.\n\nAndroid ${android.os.Build.VERSION.RELEASE} · ${android.os.Build.MANUFACTURER} ${android.os.Build.MODEL}\n\nAudio : AndroidX Media3 (Apache 2.0).\nhttps://github.com/androidx/media").setPositiveButton("Fermer", null).setNeutralButton("Diagnostic") { _, _ ->
            val text = "Radio IN · Bêta 1.1 Android\nAndroid ${android.os.Build.VERSION.RELEASE}\n${statusView.text}\n${repo.status}\n\n" + runCatching { log.readText().takeLast(12000) }.getOrDefault("Aucun incident enregistré.")
            AlertDialog.Builder(this).setTitle("Diagnostic local").setMessage(text).setNegativeButton("Fermer", null).setPositiveButton("Partager…") { _, _ -> startActivity(Intent.createChooser(Intent(Intent.ACTION_SEND).setType("text/plain").putExtra(Intent.EXTRA_TEXT, text), "Partager le diagnostic")) }.show()
        }.show()
    }
    private fun checkForUpdate(manual: Boolean = false) {
        val checkedAt = System.currentTimeMillis()
        if (!manual && checkedAt - store.lastUpdateCheck < 6 * 60 * 60 * 1000) return
        store.lastUpdateCheck = checkedAt
        repo.io.execute {
            val result = runCatching {
                val c = java.net.URL("https://api.github.com/repos/Inmouved/Radio-IN/releases?per_page=100").openConnection() as java.net.HttpURLConnection
                c.connectTimeout = 10000; c.readTimeout = 15000; c.setRequestProperty("Accept", "application/vnd.github+json"); c.setRequestProperty("User-Agent", "RadioIN-Android/2")
                try {
                    require(c.responseCode == 200) { "GitHub HTTP ${c.responseCode}" }
                    val a = org.json.JSONArray(c.inputStream.use { it.readLimited(4 * 1024 * 1024) }.toString(Charsets.UTF_8))
                    (0 until a.length()).map { a.getJSONObject(it) }.filter { !it.optBoolean("draft") && Regex("^android-v(\\d+)\\.(\\d+)\\.(\\d+)-beta$", RegexOption.IGNORE_CASE).matches(it.optString("tag_name")) }
                        .mapNotNull { r ->
                            val m = Regex("^android-v(\\d+)\\.(\\d+)\\.(\\d+)-beta$", RegexOption.IGNORE_CASE).matchEntire(r.optString("tag_name")) ?: return@mapNotNull null
                            val v = m.groupValues.drop(1).map { it.toInt() }; val apk = r.optJSONArray("assets")?.let { assets -> (0 until assets.length()).map { assets.getJSONObject(it) }.firstOrNull { it.optString("name").endsWith("-Android.apk") } } ?: return@mapNotNull null
                            Triple(v, r, apk)
                        }.maxByOrNull { it.first[0] * 1000000 + it.first[1] * 1000 + it.first[2] }
                } finally { c.disconnect() }
            }
            main.post {
                if (isDestroyed) return@post
                result.fold({ release ->
                    if (release == null || compareVersion(release.first, listOf(1, 1, 0)) <= 0) { if (manual) message("Aucune mise à jour Android plus récente n’est disponible.") }
                    else {
                        val asset = release.third; val url = asset.optString("browser_download_url")
                        val tag = release.second.optString("tag_name")
                        if (!url.startsWith("https://github.com/Inmouved/Radio-IN/releases/download/$tag/")) { if (manual) message("Lien de mise à jour non reconnu."); return@fold }
                        AlertDialog.Builder(this).setTitle("Mise à jour Android disponible").setMessage("${release.second.optString("name")}\n\n${release.second.optString("body").take(2500)}\n\nAndroid demandera confirmation pour installer la mise à jour.").setNegativeButton("Plus tard", null).setPositiveButton("Télécharger") { _, _ -> startActivity(Intent(Intent.ACTION_VIEW, Uri.parse(url))) }.show()
                    }
                }, { e -> repo.log("Mise à jour Android", e); if (manual) message("Vérification impossible. Vérifiez la connexion et réessayez.") })
            }
        }
    }
    private fun compareVersion(a: List<Int>, b: List<Int>): Int { for (i in 0..2) { val c = (a.getOrElse(i) { 0 }).compareTo(b.getOrElse(i) { 0 }); if (c != 0) return c }; return 0 }
    private fun message(value: String) { if (!isFinishing && !isDestroyed) Toast.makeText(this, value, Toast.LENGTH_LONG).show() }
}

class Bars(context: android.content.Context) : View(context) {
    private val paint = android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG).apply { color = Color.rgb(199, 237, 133); strokeCap = android.graphics.Paint.Cap.ROUND }
    var running = false
        set(v) { if (field != v) { field = v; invalidate() } }
    override fun onDraw(canvas: android.graphics.Canvas) {
        super.onDraw(canvas); paint.strokeWidth = width / 7f
        for (i in 0..2) { val fraction = if (running) .25f + .7f * kotlin.math.abs(kotlin.math.sin(SystemClock.uptimeMillis() / 240.0 + i * 1.7)).toFloat() else .2f; val x = width * (i + 1) / 4f; canvas.drawLine(x, height * .9f, x, height * (.9f - .8f * fraction), paint) }
        if (running) postInvalidateDelayed(70)
    }
}
