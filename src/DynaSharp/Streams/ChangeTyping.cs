using DynaSharp.Mapping;

namespace DynaSharp.Streams;

/// <summary>
/// The types a typed stream reading tells apart, and how it makes what it gives of one of them. A typed read is one loop over the records, generic over this, so the
/// one-type and the mixed readings share it with no delegate per record.
/// </summary>
/// <typeparam name="TOut">What the reading gives: a <see cref="Change{T}"/> or a <see cref="MixedChange{T1, T2}"/>.</typeparam>
internal interface IChangeTypes<TOut>
{
    /// <summary>Finds which of the types an image is.</summary>
    /// <param name="image">The image, or <see langword="null"/> where the record has none.</param>
    /// <returns>The index of the type, or -1.</returns>
    public static abstract int FindType(Item? image);

    /// <summary>Reads the change of the type at an index.</summary>
    /// <param name="index">The index of the type.</param>
    /// <param name="record">The record.</param>
    /// <param name="before">Whether the old image is of the type.</param>
    /// <param name="after">Whether the new image is of the type.</param>
    /// <returns>The change.</returns>
    /// <exception cref="DynamoDbMappingException">An image of the type cannot be read as it.</exception>
    public static abstract TOut Create(int index, ChangeRecord record, bool before, bool after);
}

/// <summary>Reads typed changes from the images of a record.</summary>
internal static class ChangeTyping
{
    /// <summary>Finds whether an image is a <typeparamref name="T"/>: by its discriminator, or any image for an entity without one.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="image">The image, or <see langword="null"/>.</param>
    /// <returns>0 if it is, -1 if not.</returns>
    public static int FindType<T>(Item? image)
        where T : IDynamoDbEntity<T>
    {
        if (image is null)
        {
            return -1;
        }

        var schema = DiscriminatorSchema.Of<T>.Schema;
        return schema.TellsApart ? schema.FindType(image) : 0;
    }

    /// <summary>Finds which type of a mix an image is.</summary>
    /// <param name="schema">The schema of the mix.</param>
    /// <param name="image">The image, or <see langword="null"/>.</param>
    /// <returns>The index of the type, or -1.</returns>
    public static int FindType(DiscriminatorSchema schema, Item? image) => image is null ? -1 : schema.FindType(image);

    /// <summary>Reads the change of a <typeparamref name="T"/> from a record whose images say which sides are one.</summary>
    /// <typeparam name="T">The entity.</typeparam>
    /// <param name="record">The record, which has <see cref="ChangeRecord.DynamoDb"/>.</param>
    /// <param name="before">Whether the old image is a <typeparamref name="T"/>.</param>
    /// <param name="after">Whether the new image is a <typeparamref name="T"/>.</param>
    /// <returns>The change.</returns>
    /// <exception cref="DynamoDbMappingException">An image of the type cannot be read as it, with <see cref="DynamoDbMappingException.Key"/> the keys of the record.</exception>
    public static Change<T> Create<T>(ChangeRecord record, bool before, bool after)
        where T : IDynamoDbEntity<T>
    {
        var data = record.DynamoDb!;

        // Both sides of the type are a MODIFY. One side is an insert or a removal of the type, also within a MODIFY whose other image is of another type, which is a change of
        // the type at the key; a MODIFY whose other image the view leaves out stays a MODIFY.
        OperationType kind;
        if (before == after)
        {
            kind = OperationType.Modify;
        }
        else if (record.EventName == OperationType.Modify && (before ? data.NewImage : data.OldImage) is null)
        {
            kind = OperationType.Modify;
        }
        else
        {
            kind = before ? OperationType.Remove : OperationType.Insert;
        }

        return new Change<T>
        {
            Kind = kind,
            Before = before ? Read<T>(data.OldImage!, data.Keys) : default,
            After = after ? Read<T>(data.NewImage!, data.Keys) : default,
            Keys = data.Keys,
            SequenceNumber = data.SequenceNumber,
            Record = record,
        };
    }

    /// <summary>
    /// Gives the changes of a record for the types: none when no image is of one, one when both images are of the same type or only one is, and two when the type changed at
    /// the key, the removal of the old type first. A change that cannot be read throws, or, with <paramref name="onMappingFailure"/>, is handed to it and left out.
    /// </summary>
    /// <typeparam name="TTypes">The types.</typeparam>
    /// <typeparam name="TOut">What a change is.</typeparam>
    /// <param name="change">The record, with the shard it came from, which <paramref name="onMappingFailure"/> gets.</param>
    /// <param name="onMappingFailure">What takes a change that cannot be read, or <see langword="null"/> to throw.</param>
    /// <param name="first">The first change.</param>
    /// <param name="second">The second change, of a type that changed.</param>
    /// <returns>How many changes there are: 0, 1 or 2, the left out ones not counted.</returns>
    /// <exception cref="DynamoDbMappingException">A change cannot be read, and <paramref name="onMappingFailure"/> is <see langword="null"/>.</exception>
    public static int Split<TTypes, TOut>(ShardChangeRecord change, Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure, out TOut first, out TOut second)
        where TTypes : IChangeTypes<TOut>
    {
        var record = change.Record;
        first = default!;
        second = default!;
        if (record.DynamoDb is not { } data)
        {
            return 0;
        }

        var old = TTypes.FindType(data.OldImage);
        var @new = TTypes.FindType(data.NewImage);
        var count = 0;
        if (old == @new)
        {
            if (old >= 0 && TryCreate<TTypes, TOut>(old, change, true, true, onMappingFailure, out first))
            {
                count++;
            }

            return count;
        }

        if (old >= 0 && TryCreate<TTypes, TOut>(old, change, true, false, onMappingFailure, out first))
        {
            count++;
        }

        if (@new >= 0 && TryCreate<TTypes, TOut>(@new, change, false, true, onMappingFailure, out var inserted))
        {
            if (count == 0)
            {
                first = inserted;
            }
            else
            {
                second = inserted;
            }

            count++;
        }

        return count;
    }

    private static bool TryCreate<TTypes, TOut>(
        int index, ShardChangeRecord source, bool before, bool after, Action<DynamoDbMappingException, ShardChangeRecord>? onMappingFailure, out TOut change)
        where TTypes : IChangeTypes<TOut>
    {
        try
        {
            change = TTypes.Create(index, source.Record, before, after);
            return true;
        }
        catch (DynamoDbMappingException failure) when (onMappingFailure is not null)
        {
            onMappingFailure(failure, source);
            change = default!;
            return false;
        }
    }

    private static T Read<T>(Item image, IReadOnlyDictionary<string, AttributeValue>? keys)
        where T : IDynamoDbEntity<T>
    {
        try
        {
            return image.To<T>();
        }
        catch (DynamoDbMappingException failure)
        {
            // A copy, as the key of a failed read of a table is: the keys of the record stay as they are.
            if (keys is not null)
            {
                failure.Key = new Item(keys);
            }

            throw;
        }
    }
}
