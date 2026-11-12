package org.radioin.player

import android.app.Application
import org.junit.Assert.*
import org.junit.Before
import org.junit.Test
import org.junit.runner.RunWith
import org.json.JSONObject
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [26, 28, 33, 35, 36], application = Application::class)
class StoreTest {
    private lateinit var store: Store
    @Before fun reset() { store = Store(RuntimeEnvironment.getApplication()); store.prefs.edit().clear().commit() }
    @Test fun defaultsDoNotStartPlaybackOrNotifications() { assertFalse(store.autoPlay); assertFalse(store.notifications); assertFalse(store.keepScreen); assertEquals(0, store.mode) }
    @Test fun favoritesMergeWithoutDuplicatesAcrossWindowsAndAndroid() {
        store.add(Track("old", "Un titre", "Un artiste"))
        val backup = """{"Application":"RadioIN","Schema":1,"Version":"Bêta 1 PUBLIC","Settings":{"Favorites":[{"Id":"old","Title":"Un titre","Artist":"Un artiste"},{"Id":"new","Title":"Titre Windows","Artist":"Autre artiste"}],"Mode":3,"Design":4,"Volume":0.35}}"""
        assertEquals(1, store.importBackup(backup)); assertEquals(2, store.favorites().size)
        assertEquals(0, store.importBackup(backup)); assertEquals(3, store.mode); assertEquals(4, store.theme)
        assertEquals(.35f, store.volume); assertFalse(store.autoPlay); assertFalse(store.notifications)
        val exported = JSONObject(store.export()); assertEquals(2, exported.getJSONObject("Settings").getJSONArray("Favorites").length())
    }
    @Test fun invalidBackupDoesNotModifyExistingData() {
        store.add(Track("keep", "À garder", "Artiste")); val before = store.export()
        listOf("{}", "{bad json", """{"Application":"Other","Schema":1} """, """{"Application":"RadioIN","Schema":1,"Settings":{"Favorites":[{"Id":"","Title":"bad"}],"Volume":3,"Design":1,"Mode":1}}""").forEach {
            assertTrue(runCatching { store.importBackup(it) }.isFailure); assertEquals(before, store.export())
        }
    }
    @Test fun removingFavoriteAndPersistingOptionsWorks() { store.add(Track("one", "Titre", "Artiste")); store.mode = 2; store.theme = 5; store.volume = .7f; store.remove("one"); val again = Store(RuntimeEnvironment.getApplication()); assertTrue(again.favorites().isEmpty()); assertEquals(2, again.mode); assertEquals(5, again.theme); assertEquals(.7f, again.volume) }
    @Test fun sizeLimitPreventsOversizedDocuments() { assertTrue(runCatching { java.io.ByteArrayInputStream(ByteArray(20)).readLimited(10) }.isFailure); assertEquals(5, java.io.ByteArrayInputStream(ByteArray(5)).readLimited(10).size) }
    @Test fun artworkUsesHttpsAndNormalizesRadioHostOnly() { assertEquals("", Track.secureArt("http://example.com/cover.jpg")); assertEquals("https://radio-in.duckdns.org/a.jpg", Track.secureArt("http://radio-in.duckdns.org/a.jpg")) }
}
