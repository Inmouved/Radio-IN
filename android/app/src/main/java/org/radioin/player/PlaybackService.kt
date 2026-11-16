package org.radioin.player

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Notification
import android.app.PendingIntent
import android.content.Intent
import android.net.ConnectivityManager
import android.net.Network
import android.os.Bundle
import android.os.SystemClock
import androidx.media3.common.AudioAttributes
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MediaMetadata
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.session.MediaSession
import androidx.media3.session.MediaLibraryService
import androidx.media3.session.MediaLibraryService.MediaLibrarySession
import androidx.media3.session.MediaLibraryService.LibraryParams
import androidx.media3.session.LibraryResult
import androidx.media3.session.CommandButton
import androidx.media3.session.MediaConstants
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionResult
import androidx.media3.session.SessionError
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture
import com.google.common.collect.ImmutableList

@androidx.annotation.OptIn(UnstableApi::class)
internal fun radioLibrarySessionCommands(includeSleep: Boolean, favoriteCommand: SessionCommand, addFavoriteCommand: SessionCommand? = null) =
    MediaSession.ConnectionResult.DEFAULT_SESSION_AND_LIBRARY_COMMANDS.buildUpon()
        .add(favoriteCommand)
        .apply { addFavoriteCommand?.let { add(it) } }
        .apply { if (includeSleep) add(SessionCommand(PlaybackService.SLEEP, Bundle.EMPTY)) }
        .build()

@androidx.annotation.OptIn(UnstableApi::class)
class PlaybackService : MediaLibraryService() {
    companion object {
        const val SLEEP = "radioin.sleep"
        const val WIDGET_TOGGLE = "radioin.widget.TOGGLE"
        private const val ROOT_ID = "radioin.root"
        private const val LIVE_ID = "radioin.live"
        private const val UPCOMING_ID = "radioin.upcoming"
        private const val FAVORITES_ID = "radioin.favorites"
        var isPlaying = false
            private set
        var sleepDeadline = 0L
            private set
        var connection = "Prêt à écouter"
            private set
    }
    private lateinit var player: ExoPlayer
    private var session: MediaLibrarySession? = null
    private lateinit var app: RadioApp
    private var attempts = 0
    private var wantsPlayback = false
    private var previousSong = ""
    private var observedPosition = 0L
    private var stalledAt = 0L
    private var upcoming = emptyList<RadioRepository.QueuedTrack>()
    private var upcomingLoading = false
    private var upcomingCheckedAt: Long? = null
    private var upcomingUpdatedAt: Long? = null
    private var upcomingFailed = false
    private lateinit var connectivity: ConnectivityManager
    private val networkRetry = Runnable {
        app.repo.refresh()
        if (upcomingCheckedAt != null && upcomingFailed) {
            upcomingCheckedAt = null
            refreshUpcoming()
        }
        if (wantsPlayback && !player.isPlaying && player.playbackState in listOf(Player.STATE_IDLE, Player.STATE_BUFFERING)) {
            app.repo.main.removeCallbacks(retry); attempts = 0
            player.stop(); player.prepare(); player.play(); connection = "Réseau rétabli · reconnexion…"; app.repo.changed()
        }
    }
    private val networkLost = Runnable { if (wantsPlayback && !player.isPlaying) scheduleRetry() }
    private val networkCallback = object : ConnectivityManager.NetworkCallback() {
        override fun onAvailable(network: Network) {
            app.repo.main.removeCallbacks(networkLost)
            app.repo.main.removeCallbacks(networkRetry)
            app.repo.main.postDelayed(networkRetry, 1200)
        }
        override fun onLost(network: Network) {
            if (wantsPlayback) {
                connection = "Réseau interrompu · reconnexion automatique…"; app.repo.changed()
                app.repo.main.removeCallbacks(networkLost); app.repo.main.postDelayed(networkLost, 4500)
            }
        }
    }
    private val metadataListener: () -> Unit = { updateMetadata() }
    private val requestFavoriteCommand = SessionCommand("radioin.requestFavorite", Bundle.EMPTY)
    private val addFavoriteCommand = SessionCommand("radioin.addFavorite", Bundle.EMPTY)
    private val favoriteRequestButton = CommandButton.Builder(CommandButton.ICON_PLAYLIST_ADD)
        .setDisplayName("Demander ce titre").setSessionCommand(requestFavoriteCommand).build()
    private val addFavoriteButton = CommandButton.Builder(CommandButton.ICON_HEART_UNFILLED)
        .setDisplayName("Ajouter aux favoris").setSessionCommand(addFavoriteCommand).build()
    private val libraryCallback = object : MediaLibrarySession.Callback {
        override fun onConnect(session: MediaSession, controller: MediaSession.ControllerInfo): MediaSession.ConnectionResult {
            val accepted = controller.packageName == packageName || controller.isTrusted || controller.packageName == "com.google.android.projection.gearhead"
            if (!accepted) return MediaSession.ConnectionResult.reject()
            // Android Auto needs the library commands as well as playback commands to load
            // the root and its children. DEFAULT_SESSION_COMMANDS alone leaves its browser
            // spinning and it eventually reports that the app is unavailable.
            val commands = radioLibrarySessionCommands(controller.packageName == packageName, requestFavoriteCommand, addFavoriteCommand)
            return MediaSession.ConnectionResult.AcceptedResultBuilder(session)
                .setAvailableSessionCommands(commands)
                .setAvailablePlayerCommands(MediaSession.ConnectionResult.DEFAULT_PLAYER_COMMANDS)
                .build()
        }
        override fun onCustomCommand(session: MediaSession, controller: MediaSession.ControllerInfo, customCommand: SessionCommand, args: Bundle): ListenableFuture<SessionResult> {
            if (customCommand.customAction == SLEEP && controller.packageName == packageName) {
                val minutes = args.getInt("minutes").coerceIn(0, 180)
                sleepDeadline = if (minutes == 0) 0 else SystemClock.elapsedRealtime() + minutes * 60000L
                app.repo.changed(); return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
            if (controller.packageName != "com.google.android.projection.gearhead") {
                return Futures.immediateFuture(SessionResult(SessionError.ERROR_NOT_SUPPORTED))
            }
            val mediaId = args.getString(MediaConstants.EXTRA_KEY_MEDIA_ID)
            if (customCommand.customAction == addFavoriteCommand.customAction && mediaId == LIVE_ID) {
                app.store.add(app.repo.current)
                app.repo.changed()
                return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
            val id = mediaId?.removePrefix("favorite:")
            val track = app.store.favorites().firstOrNull { it.id == id }
            if (customCommand.customAction != requestFavoriteCommand.customAction || track == null) {
                return Futures.immediateFuture(SessionResult(SessionError.ERROR_NOT_SUPPORTED))
            }
            val future = com.google.common.util.concurrent.SettableFuture.create<SessionResult>()
            requestFavorite(track) { ok -> future.set(SessionResult(if (ok) SessionResult.RESULT_SUCCESS else SessionError.ERROR_UNKNOWN)) }
            return future
        }
        override fun onSetMediaItems(session: MediaSession, controller: MediaSession.ControllerInfo, mediaItems: List<MediaItem>, startIndex: Int, startPositionMs: Long): ListenableFuture<MediaSession.MediaItemsWithStartPosition> {
            // Assistant and Android Auto may provide a search-only media item for a
            // voice request. Radio IN has one live stream, so every such request
            // resolves safely to the current live item.
            val resolved = mediaItems.map { requested ->
                if (requested.mediaId == LIVE_ID || !requested.requestMetadata.searchQuery.isNullOrBlank()) item(app.repo.current) else requested
            }
            return Futures.immediateFuture(MediaSession.MediaItemsWithStartPosition(resolved, 0, startPositionMs))
        }
        override fun onGetLibraryRoot(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, params: LibraryParams?): ListenableFuture<LibraryResult<MediaItem>> {
            val root = browseItem(ROOT_ID, "Radio IN", browsable = true, playable = false)
            return Futures.immediateFuture(LibraryResult.ofItem(root, params))
        }
        override fun onGetChildren(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, parentId: String, page: Int, pageSize: Int, params: LibraryParams?): ListenableFuture<LibraryResult<ImmutableList<MediaItem>>> {
            val items = when (parentId) {
                ROOT_ID -> rootItems()
                UPCOMING_ID -> {
                    refreshUpcoming()
                    upcomingItems()
                }
                FAVORITES_ID -> app.store.favorites().map { favoriteItem(it) }
                else -> emptyList()
            }
            val size = pageSize.coerceAtLeast(1)
            val start = (page.coerceAtLeast(0).toLong() * size).coerceAtMost(items.size.toLong()).toInt()
            return Futures.immediateFuture(LibraryResult.ofItemList(ImmutableList.copyOf(items.drop(start).take(size)), params))
        }
        override fun onGetItem(session: MediaLibrarySession, browser: MediaSession.ControllerInfo, mediaId: String): ListenableFuture<LibraryResult<MediaItem>> {
            val item = when (mediaId) {
                ROOT_ID -> browseItem(ROOT_ID, "Radio IN", true, false)
                LIVE_ID -> item(app.repo.current)
                UPCOMING_ID -> browseItem(UPCOMING_ID, "À venir", true, false)
                FAVORITES_ID -> browseItem(FAVORITES_ID, "Mes favoris", true, false)
                else -> when {
                    mediaId.startsWith("favorite:") -> app.store.favorites().firstOrNull { it.id == mediaId.removePrefix("favorite:") }?.let { favoriteItem(it) }
                    mediaId.startsWith("upcoming:") -> upcomingItems().firstOrNull { it.mediaId == mediaId }
                    else -> null
                }
            }
            return Futures.immediateFuture(if (item == null) LibraryResult.ofError(SessionError.ERROR_BAD_VALUE) else LibraryResult.ofItem(item, null))
        }
    }
    internal fun rootItems() = listOf(
        browseItem(LIVE_ID, "RADIO IN", false, true),
        browseItem(UPCOMING_ID, "À venir", true, false),
        browseItem(FAVORITES_ID, "Mes favoris", true, false)
    )

    private fun upcomingItems(): List<MediaItem> {
        val now = System.currentTimeMillis() / 1000
        if (upcomingUpdatedAt?.let { now - it in -300..100 } == true) {
            if (upcoming.isNotEmpty()) return upcoming.map(::upcomingItem)
            if (!upcomingFailed && !upcomingLoading) {
                return listOf(browseItem("upcoming:empty", "Aucun titre annoncé", false, false))
            }
        }
        val title = if (upcomingLoading) "Chargement de la file…" else "File momentanément indisponible"
        return listOf(browseItem("upcoming:status", title, false, false))
    }

    private fun refreshUpcoming() {
        val activeSession = session ?: return
        val now = SystemClock.elapsedRealtime()
        if (upcomingLoading || upcomingCheckedAt?.let { now - it < 15000 } == true) return
        upcomingLoading = true
        app.repo.upcoming { response ->
            if (session !== activeSession) return@upcoming
            upcomingLoading = false
            upcomingCheckedAt = SystemClock.elapsedRealtime()
            response.fold({ snapshot ->
                upcoming = snapshot.tracks.take(5)
                upcomingUpdatedAt = snapshot.updated
                upcomingFailed = snapshot.stale
            }, { error ->
                upcomingFailed = true
                app.repo.log("File Android Auto", error)
            })
            activeSession.notifyChildrenChanged(UPCOMING_ID, upcomingItems().size, null)
        }
    }
    private val retry = Runnable {
        if (wantsPlayback) { player.prepare(); player.play(); connection = "Reconnexion…"; app.repo.changed() }
    }
    private val timer = object : Runnable {
        override fun run() {
            if (sleepDeadline > 0 && SystemClock.elapsedRealtime() >= sleepDeadline) {
                sleepDeadline = 0; wantsPlayback = false; player.pause(); player.stop(); connection = "Minuterie terminée"; app.repo.log("Arrêt par minuterie"); app.repo.changed()
            }
            if (wantsPlayback && player.isPlaying) {
                val position = player.currentPosition
                if (position != observedPosition) { stalledAt = SystemClock.elapsedRealtime(); observedPosition = position }
                else if (SystemClock.elapsedRealtime() - stalledAt > 30000) { app.repo.log("Flux interrompu détecté"); player.stop(); scheduleRetry() }
            } else stalledAt = SystemClock.elapsedRealtime()
            app.repo.main.postDelayed(this, 1000)
        }
    }
    override fun onCreate() {
        super.onCreate(); app = application as RadioApp
        connectivity = getSystemService(ConnectivityManager::class.java)
        player = ExoPlayer.Builder(this).build().apply {
            setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
            setHandleAudioBecomingNoisy(true)
            setWakeMode(C.WAKE_MODE_LOCAL)
            volume = app.store.volume
            setMediaItem(item(app.repo.current))
        }
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        session = MediaLibrarySession.Builder(this, player, libraryCallback).setSessionActivity(open)
            .setCommandButtonsForMediaItems(listOf(addFavoriteButton, favoriteRequestButton)).build()
        player.addListener(object : Player.Listener {
            override fun onPlayWhenReadyChanged(playWhenReady: Boolean, reason: Int) {
                wantsPlayback = playWhenReady
                app.repo.main.removeCallbacks(retry)
                if (playWhenReady && player.playbackState == Player.STATE_IDLE) player.prepare()
                if (!playWhenReady) { attempts = 0; if (player.playbackState != Player.STATE_IDLE) player.stop(); connection = "Arrêté" }
                app.repo.playbackActive = playWhenReady; app.repo.changed(); RadioWidgetProvider.refresh(this@PlaybackService)
            }
            override fun onIsPlayingChanged(isPlaying: Boolean) {
                if (isPlaying) { attempts = 0; connection = "En direct" }
                PlaybackService.isPlaying = isPlaying
                app.repo.changed(); RadioWidgetProvider.refresh(this@PlaybackService)
            }
            override fun onPlaybackStateChanged(state: Int) {
                if (state == Player.STATE_BUFFERING) connection = "Connexion au direct…"
                if (state == Player.STATE_ENDED && wantsPlayback) scheduleRetry()
                PlaybackService.isPlaying = player.isPlaying
                app.repo.changed(); RadioWidgetProvider.refresh(this@PlaybackService)
            }
            override fun onPlayerError(error: PlaybackException) { app.repo.log("Lecture audio", error); if (wantsPlayback) scheduleRetry() }
        })
        app.repo.listeners.add(metadataListener); updateMetadata(); app.repo.refresh(); app.repo.main.post(timer)
        runCatching { connectivity.registerDefaultNetworkCallback(networkCallback, app.repo.main) }
            .onFailure { app.repo.log("Surveillance du réseau", it) }
    }
    private fun scheduleRetry() {
        app.repo.main.removeCallbacks(retry)
        val seconds = (3 * (1 shl attempts.coerceAtMost(4))).coerceAtMost(30); attempts++
        connection = "Flux indisponible · nouvel essai dans ${seconds}s"; app.repo.changed()
        app.repo.main.postDelayed(retry, seconds * 1000L)
    }
    private fun browseItem(id: String, title: String, browsable: Boolean, playable: Boolean) = MediaItem.Builder().setMediaId(id).setMediaMetadata(
        MediaMetadata.Builder().setTitle(title).setIsBrowsable(browsable).setIsPlayable(playable).build()
    ).build()
    private fun favoriteItem(track: Track) = MediaItem.Builder().setMediaId("favorite:${track.id}").setMediaMetadata(
        MediaMetadata.Builder().setTitle(track.title).setArtist(track.artist).setArtworkUri(track.art.takeIf { it.isNotBlank() }?.let { android.net.Uri.parse(it) })
            .setIsBrowsable(false).setIsPlayable(false).setSupportedCommands(listOf(requestFavoriteCommand.customAction)).build()
    ).build()
    private fun upcomingItem(queued: RadioRepository.QueuedTrack) = MediaItem.Builder().setMediaId("upcoming:${queued.track.id}").setMediaMetadata(
        MediaMetadata.Builder().setTitle(queued.track.title).setArtist(queued.track.artist)
            .setAlbumTitle(if (queued.requested) "Demande auditeur" else "À venir")
            .setArtworkUri(queued.track.art.takeIf { it.isNotBlank() }?.let { android.net.Uri.parse(it) })
            .setIsBrowsable(false).setIsPlayable(false).build()
    ).build()
    internal fun item(track: Track) = MediaItem.Builder().setMediaId(LIVE_ID).setUri(RadioRepository.STREAM).setMediaMetadata(
        MediaMetadata.Builder().setTitle(track.title).setArtist(track.artist).setAlbumTitle("RADIO IN").setIsBrowsable(false).setIsPlayable(true)
            .setSupportedCommands(listOf(addFavoriteCommand.customAction))
            .setArtworkUri(track.art.takeIf { it.isNotBlank() }?.let { android.net.Uri.parse(it) }).build()
    ).build()
    private fun requestFavorite(favorite: Track, page: Int = 1, done: (Boolean) -> Unit) {
        if (!app.repo.requestsEnabled || page > 50) { done(false); return }
        app.repo.catalogue(favorite.title, page) { result ->
            result.fold({ data ->
                val found = data.tracks.firstOrNull { it.id == favorite.id }
                if (found != null) app.repo.request(found) { requested -> done(requested.getOrNull()?.accepted == true) }
                else if (page < data.pages) requestFavorite(favorite, page + 1, done) else done(false)
            }, { error -> app.repo.log("Demande depuis Android Auto", error); done(false) })
        }
    }
    private fun updateMetadata() {
        val t = app.repo.current
        if (previousSong == t.id || t.id.isBlank()) return
        val previous = previousSong; previousSong = t.id
        player.replaceMediaItem(0, item(t))
        RadioWidgetProvider.refresh(this)
        if (previous.isNotBlank() && app.store.notifications && player.isPlaying && (android.os.Build.VERSION.SDK_INT < 33 || checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == android.content.pm.PackageManager.PERMISSION_GRANTED)) {
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(NotificationChannel("titles", "Changements de titre", NotificationManager.IMPORTANCE_LOW))
            val pending = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            manager.notify(50, Notification.Builder(this, "titles").setSmallIcon(R.drawable.ic_radio).setContentTitle(t.title).setContentText(t.artist).setContentIntent(pending).setAutoCancel(true).setTimeoutAfter(8000).build())
        }
    }
    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaLibrarySession? = session
    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        if (intent?.action == android.provider.MediaStore.INTENT_ACTION_MEDIA_PLAY_FROM_SEARCH) {
            player.setMediaItem(item(app.repo.current)); player.prepare(); player.play()
            return START_NOT_STICKY
        }
        if (intent?.action == WIDGET_TOGGLE) {
            if (player.playWhenReady) {
                player.pause()
                player.stop()
            } else {
                if (player.playbackState == Player.STATE_IDLE) {
                    player.prepare()
                }
                player.play()
            }
            return START_NOT_STICKY
        }
        return super.onStartCommand(intent, flags, startId)
    }
    override fun onDestroy() {
        app.repo.listeners.remove(metadataListener); app.repo.main.removeCallbacks(timer); app.repo.main.removeCallbacks(retry); app.repo.main.removeCallbacks(networkRetry); app.repo.main.removeCallbacks(networkLost)
        runCatching { connectivity.unregisterNetworkCallback(networkCallback) }
        sleepDeadline = 0; wantsPlayback = false; isPlaying = false; app.repo.playbackActive = false
        player.release(); session?.release(); session = null; super.onDestroy()
    }
}
