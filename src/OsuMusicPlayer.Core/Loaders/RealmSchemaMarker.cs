using Realms;

namespace OsuMusicPlayer.Core.Loaders;

// Realm's package requires at least one generated schema type at build time even
// though the production reader uses an unrelated on-disk schema dynamically.
internal partial class RealmSchemaMarker : IRealmObject
{
    [PrimaryKey]
    public int Id { get; set; }
}
