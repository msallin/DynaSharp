using System.Runtime.CompilerServices;
using DynaSharp.Fluent;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.UnitTests.Fluent;

/// <summary>
/// The state of a command that does not convert to a <see cref="DynaSharp.Expressions.CommandHandle"/>, read from its field: a test that builds a command and reads a response or finishes it
/// without sending it needs the state, which the library keeps to itself.
/// </summary>
internal static class CommandStates
{
    public static CommandState Of<T>(GetCommand<T> command)
        where T : IDynamoDbDocument<T> => Fields<T>.Of(ref command)!;

    public static CommandState Of<T>(BatchGetCommand<T> command)
        where T : IDynamoDbDocument<T> => Fields<T>.Of(ref command)!;

    public static CommandState Of<T>(BatchWriteCommand<T> command)
        where T : IDynamoDbDocument<T> => Fields<T>.Of(ref command)!;

    public static CommandState Of<TValue, TConverter>(GetSelectCommand<TValue, TConverter> command)
        where TConverter : IDynamoDbConverter<TValue> => Fields<TValue, TConverter>.Of(ref command)!;

    public static CommandState Of<TValue, TConverter>(QuerySelectCommand<TValue, TConverter> command)
        where TConverter : IDynamoDbConverter<TValue> => Fields<TValue, TConverter>.Of(ref command)!;

    public static CommandState Of<TValue, TConverter>(ScanSelectCommand<TValue, TConverter> command)
        where TConverter : IDynamoDbConverter<TValue> => Fields<TValue, TConverter>.Of(ref command)!;

    public static CommandState Of<TValue, TConverter>(ReturnCommand<TValue, TConverter> command)
        where TConverter : IDynamoDbConverter<TValue> => Fields<TValue, TConverter>.Of(ref command)!;

    public static CommandState Of(TransactGetCommand command) => Of(ref command)!;

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
    private static extern ref CommandState? Of(ref TransactGetCommand command);

    private static class Fields<T>
        where T : IDynamoDbDocument<T>
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref GetCommand<T> command);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref BatchGetCommand<T> command);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref BatchWriteCommand<T> command);
    }

    private static class Fields<TValue, TConverter>
        where TConverter : IDynamoDbConverter<TValue>
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref GetSelectCommand<TValue, TConverter> command);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref QuerySelectCommand<TValue, TConverter> command);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref ScanSelectCommand<TValue, TConverter> command);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_state")]
        public static extern ref CommandState? Of(ref ReturnCommand<TValue, TConverter> command);
    }
}
