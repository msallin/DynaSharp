using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp;

/// <summary>What a handle can do depending on the type of its attribute.</summary>
public static class AttrExtensions
{
    /// <summary>
    /// Gets an attribute inside a nested document, by the handle that its document type generates:
    /// <c>Order.Attrs.ShipTo.At(Address.Attrs.City)</c> has the path <c>ShipTo.City</c>, and it can go on from there. A handle that names a
    /// whole document stays an ordinary attribute, which is why this is a call: <c>Set(Order.Attrs.ShipTo, address)</c> replaces the map.
    /// </summary>
    /// <typeparam name="TDocument">The document. A handle of a scalar has nothing inside, so it does not compile.</typeparam>
    /// <typeparam name="TConverter">The converter of the document.</typeparam>
    /// <typeparam name="TValue">The type of the inner attribute.</typeparam>
    /// <typeparam name="TValueConverter">The converter of the inner attribute.</typeparam>
    /// <param name="document">The attribute that holds the document.</param>
    /// <param name="attribute">The attribute inside the document, a name or a path. Nothing checks that it belongs to <typeparamref name="TDocument"/>.</param>
    /// <returns>The attribute at the nested path.</returns>
    public static Attr<TValue, TValueConverter> At<TDocument, TConverter, TValue, TValueConverter>(this Attr<TDocument, TConverter> document, Attr<TValue, TValueConverter> attribute)
        where TDocument : IDynamoDbDocument<TDocument>
        where TConverter : IDynamoDbConverter<TDocument>
        where TValueConverter : IDynamoDbConverter<TValue> => new(document.Path + "." + attribute.Path);
}
