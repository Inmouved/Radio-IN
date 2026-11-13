package org.radioin.player

import android.app.PendingIntent
import android.appwidget.AppWidgetManager
import android.appwidget.AppWidgetProvider
import android.content.ComponentName
import android.content.Context
import android.content.Intent
import android.widget.RemoteViews

class RadioWidgetProvider : AppWidgetProvider() {
    override fun onUpdate(context: Context, manager: AppWidgetManager, ids: IntArray) = update(context, manager, ids)

    companion object {
        fun refresh(context: Context) {
            val manager = AppWidgetManager.getInstance(context)
            update(context, manager, manager.getAppWidgetIds(ComponentName(context, RadioWidgetProvider::class.java)))
        }

        private fun update(context: Context, manager: AppWidgetManager, ids: IntArray) {
            if (ids.isEmpty()) return
            val app = context.applicationContext as RadioApp
            val track = app.repo.current
            val views = RemoteViews(context.packageName, R.layout.radio_widget)
            views.setTextViewText(R.id.widget_title, track.title)
            views.setTextViewText(R.id.widget_artist, track.artist.ifBlank { "Radio IN · En direct" })
            val playing = PlaybackService.isPlaying
            views.setTextViewText(R.id.widget_toggle, context.getString(if (playing) R.string.widget_pause else R.string.widget_play))
            views.setContentDescription(R.id.widget_toggle, context.getString(if (playing) R.string.widget_pause_description else R.string.widget_play_description))
            val action = Intent(context, PlaybackService::class.java).setAction(PlaybackService.WIDGET_TOGGLE)
            val pending = PendingIntent.getForegroundService(context, 7821, action, PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE)
            views.setOnClickPendingIntent(R.id.widget_toggle, pending)
            views.setOnClickPendingIntent(R.id.widget_content, PendingIntent.getActivity(context, 7822, Intent(context, MainActivity::class.java), PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE))
            ids.forEach { manager.updateAppWidget(it, views) }
        }
    }
}
