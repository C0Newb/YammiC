package co.cnewb.yammic.bike

import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData
import co.cnewb.yammic.ymcsdk.models.YmcMediaPlaybackState

class DemoMusicMetadataProvider {
    fun getMetadata(): YmcMediaMetaData = YmcMediaMetaData(
        artistName = "Demo Artist",
        albumName = "Demo Album",
        songName = "Demo Song",
        durationSeconds = 210,
        playbackState = YmcMediaPlaybackState.Playing,
        playbackRate = 1.0f,
        elapsedSeconds = 0,
    )
}
