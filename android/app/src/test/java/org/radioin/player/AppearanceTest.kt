package org.radioin.player

import android.graphics.Bitmap
import android.graphics.Canvas
import android.view.View
import org.junit.Assert.*
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.Robolectric
import org.robolectric.RobolectricTestRunner
import org.robolectric.RuntimeEnvironment
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.io.File

@RunWith(RobolectricTestRunner::class)
@Config(sdk = [35], application = TestRadioApp::class, qualifiers = "w393dp-h851dp-xhdpi")
@GraphicsMode(GraphicsMode.Mode.NATIVE)
class AppearanceTest {
    @Test fun nativeRenderingOfCompactPhoneLayout() {
        val app = RuntimeEnvironment.getApplication() as TestRadioApp
        app.repo.current = Track("preview", "Radio IN — Votre musique en direct", "Rock · Metal · Découvertes")
        val life = Robolectric.buildActivity(MainActivity::class.java).create()
        val view = life.get().window.decorView
        val width = 786; val height = 1702
        view.measure(View.MeasureSpec.makeMeasureSpec(width, View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(height, View.MeasureSpec.EXACTLY)); view.layout(0, 0, width, height)
        val bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888)
        view.draw(Canvas(bitmap))
        val target = File("build/previews/compact.png"); target.parentFile!!.mkdirs(); target.outputStream().use { bitmap.compress(Bitmap.CompressFormat.PNG, 100, it) }
        assertTrue(target.length() > 10000); assertNotEquals(0, bitmap.getPixel(width / 2, height / 2))
        life.destroy()
    }
}
