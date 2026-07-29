using Verse;

namespace RandomPlus
{
    /// <summary>
    /// Every message the mod writes to the player's log goes through here, so all
    /// of them carry the mod's name. A player reading a log, or pasting one into a
    /// bug report, has no other way to tell this fork's messages from the original
    /// mod's - and the two share a lot of message text.
    /// </summary>
    internal static class ModLog
    {
        private const string Prefix = "[RandomPlusPlus] ";

        internal static void Warning(string message) => Log.Warning(Prefix + message);

        internal static void Error(string message) => Log.Error(Prefix + message);

        /// <summary>For anything reached from inside the reroll loop, which runs up
        /// to the player's reroll limit and would otherwise fill the log.</summary>
        internal static void ErrorOnce(string message, int key) => Log.ErrorOnce(Prefix + message, key);
    }
}
