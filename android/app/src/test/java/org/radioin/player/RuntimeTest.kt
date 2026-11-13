package org.radioin.player

import android.os.Looper
import android.view.View
import android.view.ViewGroup
import android.widget.Button
import android.widget.TextView
import androidx.media3.common.util.UnstableApi
import androidx.media3.session.SessionCommand
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.Shadows
import org.robolectric.annotation.Config
import java.util.concurrent.atomic.AtomicReference

class TestRadioApp : RadioApp() {
    override fun createRepository() = FakeRepository(this)
}
class FakeRepository(app: RadioApp) : RadioRepository(app) {
    var posted = 0
    var reject = false
    override fun http(path: String, post: Boolean): Response {
        if (post) { posted++; return if (reject) Response(429, """{"success":false,"message":"Veuillez patienter"}""", 30) else Response(200, """{"success":true,"message":"Demande enregistrée"}""", 0) }
        if (path.contains("nowplaying")) return Response(200, """{"is_online":true,"now_playing":{"song":{"id":"live","title":"Un titre très long pour vérifier le lecteur et ses modes","artist":"Artiste","art":""}},"station":{"requests_enabled":true},"song_history":[{"played_at":1700000000,"song":{"id":"before","title":"Titre précédent","artist":"Artiste précédent"}}]}""", 0)
        return Response(200, """{"total_pages":2,"total":21,"rows":[{"request_url":"/api/station/1/request/123","song":{"id":"wanted","title":"Titre demandé","artist":"Artiste"}}]}""", 0)
    }
}

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [26, 28, 33, 35, 36], application = TestRadioApp::class)
@androidx.annotation.OptIn(UnstableApi::class)
class RuntimeTest {
    private val app get() = RuntimeEnvironment.getApplication() as TestRadioApp
    private fun all(view: View): List<View> = listOf(view) + if (view is ViewGroup) (0 until view.childCount).flatMap { all(view.getChildAt(it)) } else emptyList()
    private fun waitFor(condition: () -> Boolean) { repeat(300) { Shadows.shadowOf(Looper.getMainLooper()).idle(); if (condition()) return; Thread.sleep(5) }; fail("Asynchronous callback did not complete") }
    @Test fun compactMainScreenShowsItsThreeContentSectionsAndNoDisplaySelectors() {
        for (mode in 0..3) {
            app.store.mode = mode
            val lifecycle = Robolectric.buildActivity(MainActivity::class.java).create()
            val activity = lifecycle.get(); val views = all(activity.window.decorView)
            assertTrue(views.filterIsInstance<TextView>().any { it.text.toString() == "RADIO IN" })
            assertTrue(views.filterIsInstance<Button>().any { it.text.toString().contains("Écouter") })
            assertTrue(views.filterIsInstance<Button>().any { it.text.toString() == "Historique" })
            assertTrue(views.filterIsInstance<Button>().any { it.text.toString() == "À venir" })
            assertTrue(views.filterIsInstance<Button>().any { it.text.toString() == "Demander" })
            assertFalse(views.filterIsInstance<Button>().any { it.text.toString().contains("Affichage") || it.text.toString().contains("Design") })
            assertEquals(0, app.store.mode)
            lifecycle.destroy()
        }
        assertEquals(0, (app.repo as FakeRepository).posted)
    }
    @Test fun serviceCanCreateSessionAndReleaseResources() {
        val life = Robolectric.buildService(PlaybackService::class.java).create()
        assertTrue(app.repo.listeners.isNotEmpty())
        life.destroy()
        assertFalse(app.repo.playbackActive)
        assertEquals(0L, PlaybackService.sleepDeadline)
    }
    @Test fun androidAutoConnectionIncludesLibraryBrowsingAndFavoriteRequestCommands() {
        val favorite = SessionCommand("radioin.requestFavorite.test", android.os.Bundle.EMPTY)
        val commands = radioLibrarySessionCommands(includeSleep = false, favoriteCommand = favorite)
        assertTrue(commands.contains(SessionCommand.COMMAND_CODE_LIBRARY_GET_LIBRARY_ROOT))
        assertTrue(commands.contains(SessionCommand.COMMAND_CODE_LIBRARY_GET_CHILDREN))
        assertTrue(commands.contains(SessionCommand.COMMAND_CODE_LIBRARY_GET_ITEM))
        assertTrue(commands.contains(favorite))
        assertFalse(commands.contains(SessionCommand(PlaybackService.SLEEP, android.os.Bundle.EMPTY)))
        assertTrue(radioLibrarySessionCommands(true, favorite).contains(SessionCommand(PlaybackService.SLEEP, android.os.Bundle.EMPTY)))
    }
    @Test fun metadataHistoryAndCatalogueAreParsed() {
        app.repo.refresh(); waitFor { app.repo.current.id == "live" }
        assertTrue(app.repo.online); assertTrue(app.repo.requestsEnabled); assertEquals("before", app.repo.history.single().id)
        val data = AtomicReference<Catalogue>()
        app.repo.catalogue("Artiste", 2) { data.set(it.getOrThrow()) }; waitFor { data.get() != null }
        assertEquals(2, data.get().page); assertEquals(2, data.get().pages); assertEquals("/api/station/1/request/123", data.get().tracks.single().request)
    }
    @Test fun serverConfirmationAndCooldownAreRespectedWithoutRealRequests() {
        val fake = app.repo as FakeRepository; val result = AtomicReference<RequestResult>()
        val track = Track("wanted", "Titre", "Artiste", request = "/api/station/1/request/123")
        fake.request(track) { result.set(it.getOrThrow()) }; waitFor { result.get() != null }; assertTrue(result.get().accepted)
        fake.reject = true; result.set(null)
        fake.request(track) { result.set(it.getOrThrow()) }; waitFor { result.get() != null }; assertFalse(result.get().accepted); assertEquals(30, result.get().retrySeconds)
        val failure = AtomicReference<Throwable>()
        fake.request(track) { failure.set(it.exceptionOrNull()) }; waitFor { failure.get() != null }
        assertEquals(2, fake.posted)
    }
}
