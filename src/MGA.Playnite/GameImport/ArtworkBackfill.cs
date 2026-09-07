using System;
using System.Collections.Generic;

namespace MGA.Playnite.GameImport
{
    /// <summary>
    /// Decides which artwork a game already in Playnite is still missing.
    ///
    /// Playnite applies the metadata a library plugin returns only to games it
    /// is adding. A game already in the database keeps what it has, which is
    /// correct — it is how a user's own choices survive a library update — but
    /// it means artwork the plugin could not supply the first time never
    /// arrives, however many times the library is updated afterwards.
    ///
    /// Both of those happened here. Icons were added to this plugin after the
    /// library had already been imported, and on the first import 327 of 505
    /// artwork requests were redirects the plugin declined to follow, so those
    /// games have no cover. Neither would ever be fixed by a refresh.
    ///
    /// Only empty slots are filled. A cover the user chose, or that another
    /// plugin supplied, is never replaced: this repairs omissions, it does not
    /// impose MGA's opinion over someone's own.
    /// </summary>
    internal static class ArtworkBackfill
    {
        /// <summary>
        /// Whether a Playnite image field is empty and may be filled. Playnite
        /// stores a database file id here, and clears it to null or an empty
        /// string, so both count as empty.
        /// </summary>
        public static bool IsMissing(string currentImageId)
        {
            return string.IsNullOrWhiteSpace(currentImageId);
        }

        /// <summary>
        /// Which of a game's image slots this plugin should fill, given what
        /// Playnite already holds and what MGA offers.
        ///
        /// Returned as a list so the caller does one pass per game and the
        /// decision stays testable without a Playnite database.
        /// </summary>
        public static List<ArtworkSlot> Missing(
            string currentIconId,
            string currentCoverId,
            string currentBackgroundId,
            int offeredIconAssetId,
            int offeredCoverAssetId,
            int offeredBackgroundAssetId)
        {
            var slots = new List<ArtworkSlot>(3);
            if (IsMissing(currentIconId) && offeredIconAssetId > 0)
            {
                slots.Add(new ArtworkSlot(ArtworkKind.Icon, offeredIconAssetId));
            }
            if (IsMissing(currentCoverId) && offeredCoverAssetId > 0)
            {
                slots.Add(new ArtworkSlot(ArtworkKind.Cover, offeredCoverAssetId));
            }
            if (IsMissing(currentBackgroundId) && offeredBackgroundAssetId > 0)
            {
                slots.Add(new ArtworkSlot(ArtworkKind.Background, offeredBackgroundAssetId));
            }
            return slots;
        }
    }

    internal enum ArtworkKind
    {
        Icon,
        Cover,
        Background
    }

    internal sealed class ArtworkSlot
    {
        public ArtworkSlot(ArtworkKind kind, int assetId)
        {
            Kind = kind;
            AssetId = assetId;
        }

        public ArtworkKind Kind { get; }

        public int AssetId { get; }

        /// <summary>
        /// A stable name for the temporary file the bytes are written to before
        /// Playnite copies them into its own storage, which takes a path rather
        /// than bytes. The extension matters: Playnite infers the image type
        /// from it.
        /// </summary>
        public string FileName(string extension)
        {
            var suffix = string.IsNullOrWhiteSpace(extension) ? ".png" : extension;
            if (!suffix.StartsWith(".", StringComparison.Ordinal))
            {
                suffix = "." + suffix;
            }
            return "mga-" + Kind.ToString().ToLowerInvariant() + "-" + AssetId + suffix;
        }
    }
}
