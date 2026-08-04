package co.cnewb.yammic.bike

import android.service.notification.NotificationListenerService

/**
 * Stub notification listener — registered so that [android.media.session.MediaSessionManager]
 * accepts this component as an authorised caller for [android.media.session.MediaSessionManager.getActiveSessions].
 * Without a registered NotificationListenerService the call requires MEDIA_CONTENT_CONTROL,
 * a system-only permission, and always throws SecurityException for third-party apps.
 */
class YammicNotificationListenerService : NotificationListenerService()
