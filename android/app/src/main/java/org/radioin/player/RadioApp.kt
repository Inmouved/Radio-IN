package org.radioin.player

import android.app.Application
import android.content.Context
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.os.Handler
import android.os.Looper
import android.util.LruCache
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.net.URLEncoder
import java.util.concurrent.Executors

fun java.io.InputStream.readLimited(limit: Int): ByteArray {
    val output = java.io.ByteArrayOutputStream()
    val buffer = ByteArray(8192)
    while (true) { val count = read(buffer); if (count < 0) break; require(output.size() + count <= limit) { "Fichier trop volumineux" }; output.write(buffer, 0, count) }
    return output.toByteArray()
}

data class Track(val id: String, val title: String, val artist: String, val art: String = "", val request: String = "", val time: Long = 0) {
    fun json() = JSONObject().put("Id", id).put("Title", title).put("Artist", artist).put("Art", art)
    companion object {
        fun song(s: JSONObject, request: String = "", time: Long = 0) = Track(s.optString("id"), s.optString("title", "Radio IN"), s.optString("artist"), secureArt(s.optString("art")), request, time)
        fun saved(s: JSONObject) = Track(s.optString("Id"), s.optString("Title"), s.optString("Artist"), secureArt(s.optString("Art")))
        fun secureArt(value: String): String = if (value.startsWith("https://")) value else if (value.startsWith("http://radio-in.duckdns.org/")) value.replaceFirst("http:", "https:") else ""
    }
}

class Store(context: Context) {
    val prefs = context.getSharedPreferences("radioin", Context.MODE_PRIVATE)
    var theme: Int get() = prefs.getInt("theme", 0); set(v) { prefs.edit().putInt("theme", v.coerceIn(0, 5)).apply() }
    var mode: Int get() = prefs.getInt("mode", 0); set(v) { prefs.edit().putInt("mode", v.coerceIn(0, 3)).apply() }
    var volume: Float get() = prefs.getFloat("volume", 1f); set(v) { prefs.edit().putFloat("volume", v.coerceIn(0f, 1f)).apply() }
    var autoPlay: Boolean get() = prefs.getBoolean("autoPlay", false); set(v) { prefs.edit().putBoolean("autoPlay", v).apply() }
    var notifications: Boolean get() = prefs.getBoolean("notifications", false); set(v) { prefs.edit().putBoolean("notifications", v).apply() }
    var keepScreen: Boolean get() = prefs.getBoolean("keepScreen", false); set(v) { prefs.edit().putBoolean("keepScreen", v).apply() }
    var lastUpdateCheck: Long get() = prefs.getLong("lastUpdateCheck", 0L); set(v) { prefs.edit().putLong("lastUpdateCheck", v).apply() }
    fun favorites(): List<Track> = try { val a = JSONArray(prefs.getString("favorites", "[]")); (0 until a.length()).map { Track.saved(a.getJSONObject(it)) } } catch (_: Exception) { emptyList() }
    private fun saveFavorites(tracks: List<Track>) { prefs.edit().putString("favorites", JSONArray().also { a -> tracks.distinctBy { it.id }.forEach { a.put(it.json()) } }.toString()).apply() }
    fun add(t: Track) { if (t.id.isNotBlank()) saveFavorites(favorites() + t) }
    fun remove(id: String) = saveFavorites(favorites().filterNot { it.id == id })
    fun export(): String = JSONObject().put("Application", "RadioIN").put("Schema", 1).put("Version", "Bêta 1 Android").put("Settings", JSONObject().put("Design", theme).put("Mode", mode).put("Volume", volume).put("Favorites", JSONArray().also { a -> favorites().forEach { a.put(it.json()) } })).toString(2)
    fun importBackup(text: String): Int {
        require(text.toByteArray().size <= 4 * 1024 * 1024) { "Sauvegarde trop volumineuse (4 Mo maximum)." }
        val root = JSONObject(text)
        require(root.optString("Application") == "RadioIN" && root.optInt("Schema") == 1) { "Ce fichier n’est pas une sauvegarde Radio IN compatible." }
        val s = root.getJSONObject("Settings")
        val a = s.getJSONArray("Favorites")
        require(a.length() <= 10000) { "Trop de favoris." }
        val tracks = (0 until a.length()).map { Track.saved(a.getJSONObject(it)) }
        require(tracks.all { it.id.isNotBlank() && it.id.length <= 128 && it.title.isNotBlank() && it.title.length <= 1024 && it.artist.length <= 1024 }) { "Favori invalide." }
        val volume = s.getDouble("Volume")
        require(volume.isFinite() && volume in 0.0..1.0) { "Volume invalide." }
        val design = s.getInt("Design").coerceIn(0, 5)
        val display = s.getInt("Mode").coerceIn(0, 3)
        // Validate everything before writing. Imports never activate playback or notifications.
        val old = favorites(); val merged = (old + tracks).distinctBy { it.id }
        saveFavorites(merged); theme = design; mode = display; this.volume = volume.toFloat()
        return merged.size - old.size
    }
}

data class Catalogue(val tracks: List<Track>, val page: Int, val pages: Int, val total: Int)
data class RequestResult(val accepted: Boolean, val message: String, val retrySeconds: Int)

open class RadioRepository(private val app: RadioApp) {
    companion object { const val ORIGIN = "https://radio-in.duckdns.org"; const val STREAM = "$ORIGIN/listen/radio_in/radio.mp3" }
    val io = Executors.newFixedThreadPool(3)
    val main = Handler(Looper.getMainLooper())
    val listeners = linkedSetOf<() -> Unit>()
    var current = Track("", "Radio IN", "Le direct vous attend")
    var history: List<Track> = emptyList()
    val sessionHistory = mutableListOf<Track>()
    private val sessionKeys = mutableSetOf<String>()
    var online = true
    var requestsEnabled = false
    var status = "Connexion aux informations…"
    var cooldownUntil = 0L
    private var refreshing = false
    var playbackActive = false
    private val ticker = object : Runnable {
        override fun run() { if (listeners.isNotEmpty() || playbackActive) refresh(); main.postDelayed(this, 15000) }
    }
    init { main.post(ticker) }
    fun changed() { listeners.toList().forEach { it() } }
    fun log(message: String, error: Throwable? = null) {
        try { synchronized(this) {
            val dir = File(app.filesDir, "logs").apply { mkdirs() }; val file = File(dir, "RadioIN.log")
            if (file.length() > 512 * 1024) { val previous = File(dir, "RadioIN.previous.log"); previous.delete(); file.renameTo(previous) }
            file.appendText("${java.time.Instant.now()} | $message ${error?.javaClass?.simpleName ?: ""}: ${error?.message ?: ""}\n")
        } } catch (_: Exception) { }
    }
    data class Response(val code: Int, val body: String, val retry: Int)
    open fun http(path: String, post: Boolean = false): Response {
        require(path.startsWith("/api/") && !path.startsWith("//"))
        val c = URL(ORIGIN + path).openConnection() as HttpURLConnection
        c.connectTimeout = 12000; c.readTimeout = 12000; c.instanceFollowRedirects = false
        c.setRequestProperty("Accept", "application/json"); c.setRequestProperty("User-Agent", "RadioIN-Android/1")
        if (post) { c.requestMethod = "POST"; c.doOutput = true; c.setFixedLengthStreamingMode(0); c.outputStream.close() }
        try {
            val code = c.responseCode
            val bytes = (if (code in 200..299) c.inputStream else c.errorStream)?.use { it.readLimited(2 * 1024 * 1024) } ?: byteArrayOf()
            require(bytes.size <= 2 * 1024 * 1024) { "Réponse trop volumineuse" }
            return Response(code, bytes.toString(Charsets.UTF_8), c.getHeaderField("Retry-After")?.toIntOrNull()?.coerceIn(0, 86400) ?: 0)
        } finally { c.disconnect() }
    }
    fun refresh() {
        if (refreshing) return; refreshing = true
        io.execute {
            try {
                val response = http("/api/nowplaying/radio_in"); require(response.code == 200) { "HTTP ${response.code}" }
                val root = JSONObject(response.body); val song = Track.song(root.getJSONObject("now_playing").getJSONObject("song"))
                val list = root.optJSONArray("song_history") ?: JSONArray()
                val h = (0 until list.length()).map { val item = list.getJSONObject(it); Track.song(item.getJSONObject("song"), time = item.optLong("played_at")) }
                main.post {
                    if (playbackActive && current.id.isNotBlank() && current.id != song.id) {
                        val key = current.id + "|" + System.currentTimeMillis() / 1000
                        if (sessionKeys.add(key)) { sessionHistory.add(0, current.copy(time = System.currentTimeMillis() / 1000)); if (sessionHistory.size > 500) sessionHistory.removeAt(sessionHistory.lastIndex) }
                    }
                    current = song; history = h; online = root.optBoolean("is_online"); requestsEnabled = root.getJSONObject("station").optBoolean("requests_enabled")
                    status = if (online) "Informations actualisées" else "Radio hors ligne"
                    refreshing = false; changed()
                }
            } catch (e: Exception) { log("Informations radio", e); main.post { refreshing = false; status = "Informations indisponibles · nouvel essai automatique"; changed() } }
        }
    }
    data class QueuedTrack(val track: Track, val expected: Long, val requested: Boolean)
    data class QueueSnapshot(val tracks: List<QueuedTrack>, val updated: Long, val stale: Boolean)
    fun upcoming(done: (Result<QueueSnapshot>) -> Unit) {
        io.execute {
            val result = runCatching {
                val c = URL("https://radio-in.duckdns.org:9443/queue.json").openConnection() as HttpURLConnection
                c.connectTimeout = 10000; c.readTimeout = 15000; c.setRequestProperty("Accept", "application/json"); c.setRequestProperty("User-Agent", "RadioIN-Android/2")
                try {
                    require(c.responseCode == 200) { "HTTP ${c.responseCode}" }
                    val raw = c.inputStream.use { it.readLimited(256 * 1024) }.toString(Charsets.UTF_8)
                    val j = JSONObject(raw); val now = System.currentTimeMillis() / 1000; val updated = j.optLong("updated_at")
                    require(j.optInt("schema") == 1 && j.optString("station") == "radio_in" && j.optBoolean("available") && updated in (now - 100)..(now + 300)) { "File indisponible ou périmée" }
                    val a = j.optJSONArray("items") ?: JSONArray(); require(a.length() <= 25)
                    val items = (0 until a.length()).map { i ->
                        val item = a.getJSONObject(i); val art = Track.secureArt(item.optString("art"))
                        QueuedTrack(Track("queue-$i", item.optString("title", "Titre inconnu"), item.optString("artist"), art), item.optLong("played_at"), item.optBoolean("is_request"))
                    }
                    QueueSnapshot(items, updated, j.optBoolean("stale"))
                } finally { c.disconnect() }
            }
            main.post { done(result) }
        }
    }
    fun catalogue(query: String, page: Int, done: (Result<Catalogue>) -> Unit) {
        io.execute {
            val result = runCatching {
                val r = http("/api/station/1/requests?rowCount=20&page=${page.coerceAtLeast(1)}&searchPhrase=${URLEncoder.encode(query.take(200), "UTF-8")}")
                require(r.code == 200) { "Catalogue indisponible (HTTP ${r.code})" }
                val root = JSONObject(r.body); val a = root.getJSONArray("rows")
                Catalogue((0 until a.length()).map { val item = a.getJSONObject(it); Track.song(item.getJSONObject("song"), item.optString("request_url")) }, page, root.optInt("total_pages", 1).coerceAtLeast(1), root.optInt("total"))
            }
            main.post { done(result) }
        }
    }
    fun request(track: Track, done: (Result<RequestResult>) -> Unit) {
        io.execute {
            val result = runCatching {
                require(System.currentTimeMillis() >= cooldownUntil) { "Patientez avant une nouvelle demande." }
                val r = http(track.request, true)
                val root = runCatching { JSONObject(r.body) }.getOrNull()
                val accepted = r.code in 200..299 && root?.optBoolean("success", false) == true
                val message = root?.optString("message")?.takeIf { it.isNotBlank() } ?: if (r.code in 200..299) "Confirmation du serveur non reconnue. Ne renvoyez pas la demande tout de suite." else "Demande refusée (HTTP ${r.code})."
                if (r.retry > 0) cooldownUntil = System.currentTimeMillis() + r.retry * 1000L
                RequestResult(accepted, message, r.retry)
            }
            main.post { done(result) }
        }
    }
}

class Artwork(private val repo: RadioRepository) {
    private val cache = object : LruCache<String, Bitmap>(8 * 1024 * 1024) { override fun sizeOf(key: String, value: Bitmap) = value.byteCount }
    fun load(url: String, done: (Bitmap?) -> Unit) {
        if (url.isBlank()) { done(null); return }
        cache.get(url)?.let { done(it); return }
        repo.io.execute {
            val bitmap = runCatching {
                require(url.startsWith("https://"))
                val c = URL(url).openConnection() as HttpURLConnection; c.connectTimeout = 10000; c.readTimeout = 10000; c.instanceFollowRedirects = false
                try {
                    require(c.responseCode == 200)
                    val bytes = c.inputStream.use { it.readLimited(5 * 1024 * 1024) }
                    val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }; BitmapFactory.decodeByteArray(bytes, 0, bytes.size, bounds)
                    val opts = BitmapFactory.Options().apply { inSampleSize = (maxOf(bounds.outWidth, bounds.outHeight) / 900).coerceAtLeast(1) }
                    BitmapFactory.decodeByteArray(bytes, 0, bytes.size, opts)
                } finally { c.disconnect() }
            }.getOrNull()
            if (bitmap != null) cache.put(url, bitmap)
            repo.main.post { done(bitmap) }
        }
    }
}

open class RadioApp : Application() {
    lateinit var store: Store; lateinit var repo: RadioRepository; lateinit var artwork: Artwork
    override fun onCreate() {
        super.onCreate(); store = Store(this); repo = createRepository(); artwork = Artwork(repo)
        val previous = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { t, e -> repo.log("Exception non gérée", e); previous?.uncaughtException(t, e) }
    }
    open fun createRepository() = RadioRepository(this)
}
