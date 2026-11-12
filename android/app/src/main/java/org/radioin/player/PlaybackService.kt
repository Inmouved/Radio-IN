package org.radioin.player

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Notification
import android.app.PendingIntent
import android.content.Intent
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
import androidx.media3.session.MediaSessionService
import androidx.media3.session.SessionCommand
import androidx.media3.session.SessionResult
import androidx.media3.session.SessionError
import com.google.common.util.concurrent.Futures
import com.google.common.util.concurrent.ListenableFuture

@androidx.annotation.OptIn(UnstableApi::class)
class PlaybackService : MediaSessionService() {
    companion object {
        const val SLEEP = "radioin.sleep"
        var sleepDeadline = 0L
            private set
        var connection = "Prêt à écouter"
            private set
    }
    private lateinit var player: ExoPlayer
    private var session: MediaSession? = null
    private lateinit var app: RadioApp
    private var attempts = 0
    private var wantsPlayback = false
    private var previousSong = ""
    private var observedPosition = 0L
    private var stalledAt = 0L
    private val metadataListener: () -> Unit = { updateMetadata() }
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
        player = ExoPlayer.Builder(this).build().apply {
            setAudioAttributes(AudioAttributes.Builder().setUsage(C.USAGE_MEDIA).setContentType(C.AUDIO_CONTENT_TYPE_MUSIC).build(), true)
            setHandleAudioBecomingNoisy(true)
            setWakeMode(C.WAKE_MODE_LOCAL)
            volume = app.store.volume
            setMediaItem(item(app.repo.current))
        }
        val open = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
        session = MediaSession.Builder(this, player).setSessionActivity(open).setCallback(object : MediaSession.Callback {
            override fun onConnect(session: MediaSession, controller: MediaSession.ControllerInfo): MediaSession.ConnectionResult {
                if (controller.packageName != packageName && !controller.isTrusted) return MediaSession.ConnectionResult.reject()
                return MediaSession.ConnectionResult.AcceptedResultBuilder(session).setAvailableSessionCommands(MediaSession.ConnectionResult.DEFAULT_SESSION_COMMANDS.buildUpon().add(SessionCommand(SLEEP, Bundle.EMPTY)).build()).build()
            }
            override fun onCustomCommand(session: MediaSession, controller: MediaSession.ControllerInfo, customCommand: SessionCommand, args: Bundle): ListenableFuture<SessionResult> {
                if (customCommand.customAction != SLEEP || controller.packageName != packageName) return Futures.immediateFuture(SessionResult(SessionError.ERROR_NOT_SUPPORTED))
                val minutes = args.getInt("minutes").coerceIn(0, 180)
                sleepDeadline = if (minutes == 0) 0 else SystemClock.elapsedRealtime() + minutes * 60000L
                app.repo.changed(); return Futures.immediateFuture(SessionResult(SessionResult.RESULT_SUCCESS))
            }
        }).build()
        player.addListener(object : Player.Listener {
            override fun onPlayWhenReadyChanged(playWhenReady: Boolean, reason: Int) {
                wantsPlayback = playWhenReady
                app.repo.main.removeCallbacks(retry)
                if (playWhenReady && player.playbackState == Player.STATE_IDLE) player.prepare()
                if (!playWhenReady) { attempts = 0; if (player.playbackState != Player.STATE_IDLE) player.stop(); connection = "Arrêté" }
                app.repo.playbackActive = playWhenReady; app.repo.changed()
            }
            override fun onIsPlayingChanged(isPlaying: Boolean) {
                if (isPlaying) { attempts = 0; connection = "En direct" }
                app.repo.changed()
            }
            override fun onPlaybackStateChanged(state: Int) {
                if (state == Player.STATE_BUFFERING) connection = "Connexion au direct…"
                if (state == Player.STATE_ENDED && wantsPlayback) scheduleRetry()
                app.repo.changed()
            }
            override fun onPlayerError(error: PlaybackException) { app.repo.log("Lecture audio", error); if (wantsPlayback) scheduleRetry() }
        })
        app.repo.listeners.add(metadataListener); updateMetadata(); app.repo.refresh(); app.repo.main.post(timer)
    }
    private fun scheduleRetry() {
        app.repo.main.removeCallbacks(retry)
        val seconds = (3 * (1 shl attempts.coerceAtMost(4))).coerceAtMost(30); attempts++
        connection = "Flux indisponible · nouvel essai dans ${seconds}s"; app.repo.changed()
        app.repo.main.postDelayed(retry, seconds * 1000L)
    }
    private fun item(track: Track) = MediaItem.Builder().setMediaId("radioin-live").setUri(RadioRepository.STREAM).setMediaMetadata(
        MediaMetadata.Builder().setTitle(track.title).setArtist(track.artist).setAlbumTitle("Radio IN · En direct").setIsPlayable(true).setArtworkUri(track.art.takeIf { it.isNotBlank() }?.let { android.net.Uri.parse(it) }).build()
    ).build()
    private fun updateMetadata() {
        val t = app.repo.current
        if (previousSong == t.id || t.id.isBlank()) return
        val previous = previousSong; previousSong = t.id
        player.replaceMediaItem(0, item(t))
        if (previous.isNotBlank() && app.store.notifications && player.isPlaying && (android.os.Build.VERSION.SDK_INT < 33 || checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == android.content.pm.PackageManager.PERMISSION_GRANTED)) {
            val manager = getSystemService(NotificationManager::class.java)
            manager.createNotificationChannel(NotificationChannel("titles", "Changements de titre", NotificationManager.IMPORTANCE_LOW))
            val pending = PendingIntent.getActivity(this, 0, Intent(this, MainActivity::class.java), PendingIntent.FLAG_IMMUTABLE or PendingIntent.FLAG_UPDATE_CURRENT)
            manager.notify(50, Notification.Builder(this, "titles").setSmallIcon(R.drawable.ic_radio).setContentTitle(t.title).setContentText(t.artist).setContentIntent(pending).setAutoCancel(true).setTimeoutAfter(8000).build())
        }
    }
    override fun onGetSession(controllerInfo: MediaSession.ControllerInfo): MediaSession? = session
    override fun onDestroy() {
        app.repo.listeners.remove(metadataListener); app.repo.main.removeCallbacks(timer); app.repo.main.removeCallbacks(retry)
        sleepDeadline = 0; wantsPlayback = false; app.repo.playbackActive = false
        player.release(); session?.release(); session = null; super.onDestroy()
    }
}
