using System.Text.Json.Serialization;

namespace OsuMusicPlayer.Mobile.Core;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(MobileTrackPage))]
[JsonSerializable(typeof(MobilePlayerState))]
internal partial class MobileJsonContext : JsonSerializerContext;
