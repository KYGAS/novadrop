using Vezel.Novadrop.Data.IO;
using Vezel.Novadrop.Data.Serialization.Items;

namespace Vezel.Novadrop.Data.Serialization.Regions;

sealed class DataCenterRegion<T>
    where T : unmanaged, IDataCenterItem<T>
{
    public List<T> Elements { get; } = new List<T>(ushort.MaxValue);

    public async ValueTask ReadAsync(bool strict, DataCenterBinaryReader reader, CancellationToken cancellationToken)
    {
        var capacity = await reader.ReadInt32Async(cancellationToken).ConfigureAwait(false);
        var count = await reader.ReadInt32Async(cancellationToken).ConfigureAwait(false);

        if (count < 0)
            throw new InvalidDataException($"Region length {count} is negative.");

        if (strict)
        {
            if (capacity < 0)
                throw new InvalidDataException($"Region capacity {capacity} is negative.");

            if (count > capacity)
                throw new InvalidDataException($"Region length {count} is greater than region capacity {capacity}.");
        }

        var length = Unsafe.SizeOf<T>() * capacity;
        var bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            await reader.ReadAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);

            void ProcessElements()
            {
                foreach (ref var elem in MemoryMarshal.CreateSpan(
                    ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(bytes)), count))
                {
                    if (!BitConverter.IsLittleEndian)
                        T.ReverseEndianness(ref elem);

                    Elements.Add(elem);
                }
            }

            // Cannot use refs in async methods...
            ProcessElements();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public async ValueTask WriteAsync(DataCenterBinaryWriter writer, CancellationToken cancellationToken)
    {
        var count = Elements.Count;

        // TODO: Align the physical region to e.g. page boundary even if we only use a subset?
        for (var i = 0; i < 2; i++)
            await writer.WriteInt32Async(count, cancellationToken).ConfigureAwait(false);

        var length = Unsafe.SizeOf<T>() * count;
        var bytes = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            void ProcessElements()
            {
                var i = 0;

                foreach (ref var elem in MemoryMarshal.CreateSpan(
                    ref Unsafe.As<byte, T>(ref MemoryMarshal.GetArrayDataReference(bytes)), count))
                {
                    elem = Elements[i++];

                    if (!BitConverter.IsLittleEndian)
                        T.ReverseEndianness(ref elem);
                }
            }

            // Cannot use refs in async methods...
            ProcessElements();

            await writer.WriteAsync(bytes.AsMemory(0, length), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(bytes);
        }
    }

    public T GetElement(int index)
    {
        return index < Elements.Count
            ? Elements[index]
            : throw new InvalidDataException($"Region element index {index} is out of bounds (0..{Elements.Count}).");
    }

    public void SetElement(int index, T value)
    {
        Elements[index] = value;
    }
}