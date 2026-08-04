package co.cnewb.yammic.ymcsdk.providers

import co.cnewb.yammic.ymcsdk.models.YmcMediaMetaData

interface YmcMusicMetadataProvider {
    fun getMetadata(): YmcMediaMetaData
}
