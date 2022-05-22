namespace Vezel.Novadrop.Scanners;

sealed class ResourceContainerScanner : IScanner
{
    static readonly ReadOnlyMemory<byte?> _pattern = new byte?[]
    {
        0x44, 0x8b, 0xda,                         // mov r11d, edx
        0x48, 0x8d, 0x1d, null, null, null, null, // lea rbx, [rip + <disp>]
    };

    [SuppressMessage("", "CA1308")]
    public async Task RunAsync(ScanContext context)
    {
        var exe = context.Process.MainModule;

        Console.WriteLine("Searching for resource container decryption functions...");

        var offsets = (await exe.SearchAsync(_pattern)).ToArray();

        if (offsets.Length != 2)
            throw new ApplicationException("Could not find resource container decryption functions.");

        var keys = offsets.Select(off =>
        {
            var dispOff = off + 6;

            // Resolve the RIP displacement in the instruction to an absolute address.
            var keyAddr = exe.ToAddress(dispOff + sizeof(uint)) + exe.Read<uint>(dispOff);

            if (!exe.TryGetOffset(keyAddr, out var keyOff))
                return null;

            var key = new byte[32];

            return exe.TryRead(keyOff, key) ? key : null;
        });

        if (keys.Any(k => k == null))
            throw new ApplicationException("Could not find resource container keys.");

        var strKeys = keys.Select(k => Convert.ToHexString(k!).ToLowerInvariant()).ToArray();

        foreach (var key in strKeys)
            Console.WriteLine($"Found resource container key: {key}");

        await File.WriteAllLinesAsync(Path.Combine(context.Output.FullName, "ResourceContainerKeys.txt"), strKeys);
    }
}