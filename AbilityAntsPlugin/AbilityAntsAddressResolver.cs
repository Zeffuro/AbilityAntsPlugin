using Dalamud.Game;
using System;

namespace AbilityAntsPlugin
{
    public static class AbilityAntsAddressResolver
    {
        public static IntPtr ShouldDrawAnts { get; private set; }

        public static void Setup64Bit(ISigScanner scanner)
        {
            // TODO: Swap to hook from IsActionHighlighted in CS
            ShouldDrawAnts = scanner.ScanText("E8 ?? ?? ?? ?? 88 46 41 80 BF");
        }
    }
}
