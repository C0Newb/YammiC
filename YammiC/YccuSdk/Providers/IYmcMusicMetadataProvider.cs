using CNCO.YccuSdk.Models;

namespace CNCO.YccuSdk.Providers;

public interface IYmcMusicMetadataProvider
{
    YmcMediaMetaData GetMetadata();
}
