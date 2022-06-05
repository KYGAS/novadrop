using Vezel.Novadrop.Memory;
using Vezel.Novadrop.Memory.Code;
using static Iced.Intel.AssemblerRegisters;

namespace Vezel.Novadrop.Patches;

public sealed class SecurityNeutralizationPatch : GamePatch
{
    static readonly ReadOnlyMemory<byte?> _className =
        Encoding.Unicode.GetBytes("S1SecurityCrashJob").Cast<byte?>().ToArray();

    readonly List<(nuint, nuint)> _slots = new();

    DynamicCode? _function;

    public SecurityNeutralizationPatch(NativeProcess process)
        : base(process)
    {
    }

    protected override async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var offsets = (await Executable.SearchAsync(_className, cancellationToken).ConfigureAwait(false)).ToArray();

        if (offsets.Length != 32)
            throw new GamePatchException("Could not locate security virtual method tables.");

        foreach (var off in offsets)
        {
            // The 4th slot contains the crashing method that we are interested in.
            var slot = off - (uint)Unsafe.SizeOf<nuint>() * 2;

            _slots.Add((slot, Executable.Read<nuint>(slot)));
        }
    }

    protected override Task ApplyAsync(CancellationToken cancellationToken)
    {
        // The code in the crashing method is virtualized. Instead of trying to devirtualize it, we will just replace
        // the function pointer in the virtual method table with a pointer to a no-op function that we create. A neat
        // advantage of this approach is that Themida does not seem to care about data section modifications, so we can
        // apply this change whenever.

        _function = DynamicCode.Create(Process, asm =>
        {
            asm.xor(eax, eax);
            asm.ret();
        });

        foreach (var (slot, _) in _slots)
            Executable.Write(slot, (nuint)_function.Window.Address);

        return Task.CompletedTask;
    }

    protected override Task RevertAsync(CancellationToken cancellationToken)
    {
        foreach (var (slot, original) in _slots)
            Executable.Write(slot, original);

        _function!.Dispose();

        return Task.CompletedTask;
    }
}