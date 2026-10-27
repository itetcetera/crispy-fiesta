using Mule.Engine.Core;
using Mule.Engine.Rules;
using Xunit;

namespace Mule.Engine.Tests.Original;

/// <summary>The species' walker frames (tools/species_frames.py) against the disk and the snapshot.</summary>
public class SpeciesFramesTests
{
    const int Load = 0x7BEF; // the boot image's load address (the ATR's boot header)

    static byte[] Disk() => File.ReadAllBytes(OriginalGame.ReferencePath("M.U.L.E. (1983) Electronic Arts US.atr"))[16..];

    [Fact]
    public void Tables_are_the_disks()
    {
        var d = Disk();
        int Word(int a) => d[a - Load] | d[a - Load + 1] << 8;
        for (int race = 0; race < 8; race++)
        {
            int map = Word(0x844C + 2 * race), town = Word(0x843C + 2 * race);
            Assert.Equal(d[(map - Load)..(map - Load + 82)], SpeciesFrames.Map[race]);
            Assert.Equal(d[(town - Load)..(town - Load + 264)], SpeciesFrames.Town[race]);
        }
    }

    [Fact]
    public void Snapshot_players_have_their_species_frames()
    {
        var mem = File.ReadAllBytes(OriginalGame.ReferencePath("MULE - Memory Snapshot.bin"));
        for (int p = 0; p < 4; p++)
        {
            var race = (Race)mem[0x091D + p];
            Assert.Equal(mem[(0x0E00 + p * 0x80)..(0x0E00 + (p + 1) * 0x80)], MapGraphics.WalkerMap(race));
            Assert.Equal(mem[(0x09C0 + p * 0x110)..(0x09C0 + (p + 1) * 0x110)], MapGraphics.WalkerTown(race));
        }
    }
}
