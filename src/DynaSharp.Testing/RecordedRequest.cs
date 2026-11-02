using System.Text;
using System.Text.Json;
using DynaSharp.Http;
using DynaSharp.Mapping;
using DynaSharp.Serialization;

namespace DynaSharp.Testing;

/// <summary>
/// An HTTP request that a <see cref="StubHandler"/> or a <see cref="RecordingHandler"/> received. It is a copy, so it stays readable after the client has
/// disposed the request.
/// </summary>
/// <remarks>
/// <para>
/// A test says what was sent instead of comparing JSON text: <see cref="ReadItem{T}()"/> reads the item of a put back through the code of the entity and <see cref="ReadKey()"/> the key of a get, an update or a
/// delete, <see cref="TransactItems"/> reads a transaction step by step, and <see cref="ConditionText"/>, <see cref="UpdateText"/>, <see cref="KeyConditionText"/>, <see cref="FilterText"/> and
/// <see cref="ProjectionText"/> read an expression. <see cref="BodyJson"/> reaches everything else, such as the writes of a <c>BatchWriteItem</c>.
/// </para>
/// <para>
/// <b>The notation of the texts.</b> An expression reads as DynamoDB gets it, keywords, functions and spacing included, with each placeholder replaced by what it stands for, so a test does not depend on how
/// placeholders are numbered: <c>#n0 = :v0</c> reads <c>version = 3</c>. A name stands as it is, and in backticks if it holds a dot or a bracket, because <c>`a.b`</c> is one attribute and <c>a.b</c> a path through two.
/// A value reads in the notation of PartiQL with double quotes, from its wire form, so a number keeps its text:
/// </para>
/// <list type="table">
/// <listheader><term>On the wire</term><description>In a text</description></listheader>
/// <item><term><c>{"S":"Placed"}</c></term><description><c>"Placed"</c>, with the escapes of JSON</description></item>
/// <item><term><c>{"N":"3.10"}</c></term><description><c>3.10</c></description></item>
/// <item><term><c>{"BOOL":true}</c>, <c>{"NULL":true}</c></term><description><c>true</c>, <c>null</c></description></item>
/// <item><term><c>{"B":"aGk="}</c></term><description><c>b"aGk="</c>, the base64 of the wire</description></item>
/// <item><term><c>{"L":[{"S":"a"},{"N":"1"}]}</c></term><description><c>["a", 1]</c></description></item>
/// <item><term><c>{"M":{"k":{"N":"1"}}}</c></term><description><c>{"k": 1}</c></description></item>
/// <item><term><c>{"SS":["a","b"]}</c>, <c>{"NS":["1"]}</c>, <c>{"BS":["aA=="]}</c></term><description><c>&lt;&lt;"a", "b"&gt;&gt;</c>, <c>&lt;&lt;1&gt;&gt;</c>, <c>&lt;&lt;b"aA=="&gt;&gt;</c></description></item>
/// </list>
/// <para>
/// A placeholder that the maps of the request lack stays as it is, which is the mistake a test should see. Elements of a list, entries of a map and elements of a set stay in the order of the wire, and DynamoDB does not
/// keep the last two: a test that compares a set or a map sends it through <see cref="ReadItem{T}()"/>, or holds one element.
/// </para>
/// </remarks>
public sealed class RecordedRequest
{
    private RecordedRequest(HttpMethod method, Uri? requestUri, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        Method = method;
        RequestUri = requestUri;
        Headers = headers;
        Body = body;
        Operation = ParseOperation(headers.GetValueOrDefault(WireProtocol.TargetHeaderName));
    }

    /// <summary>Gets the HTTP method. DynamoDB requests are always <c>POST</c>.</summary>
    public HttpMethod Method { get; }

    /// <summary>Gets the request URI, which for DynamoDB is the endpoint.</summary>
    public Uri? RequestUri { get; }

    /// <summary>
    /// Gets the request headers and the content headers (such as <c>Content-Type</c>) by name, ignoring case. A header with several
    /// values is joined with a comma and a space. The signature headers <c>Authorization</c>, <c>X-Amz-Date</c> and
    /// <c>X-Amz-Security-Token</c> are included.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Gets the request body, which for DynamoDB is UTF-8 JSON. Empty when the request had no content.</summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>
    /// Gets the DynamoDB operation from the <c>X-Amz-Target</c> header, for example <c>GetItem</c> for
    /// <c>DynamoDB_20120810.GetItem</c>; <see langword="null"/> if the request has no such header.
    /// </summary>
    public string? Operation { get; }

    /// <summary>Gets the request body as text.</summary>
    /// <value>The body decoded as UTF-8.</value>
    public string BodyText => Encoding.UTF8.GetString(Body.Span);

    /// <summary>Gets the request body as JSON, parsed anew on each read. The element stands on its own, so a test can keep it as long as it likes.</summary>
    /// <value>The root of the body.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public JsonElement BodyJson => JsonElement.Parse(Body.Span, new JsonDocumentOptions { MaxDepth = WireJson.ReaderOptions.MaxDepth });

    /// <summary>Gets the <c>ConditionExpression</c> with its placeholders resolved, in the notation of the remarks of this class: <c>version = 3</c>; <see langword="null"/> if the request has none.</summary>
    /// <value>The text, or <see langword="null"/>.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public string? ConditionText => RecordedJson.ResolveExpression(BodyJson, "ConditionExpression");

    /// <summary>Gets the <c>UpdateExpression</c> with its placeholders resolved, in the notation of the remarks of this class: <c>SET plan = "pro", visits = visits + 1</c>; <see langword="null"/> if the request has none.</summary>
    /// <value>The text, or <see langword="null"/>.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public string? UpdateText => RecordedJson.ResolveExpression(BodyJson, "UpdateExpression");

    /// <summary>Gets the <c>KeyConditionExpression</c> with its placeholders resolved, in the notation of the remarks of this class: <c>pk = "alice" AND begins_with(sk, "ORDER#")</c>; <see langword="null"/> if the request has none.</summary>
    /// <value>The text, or <see langword="null"/>.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public string? KeyConditionText => RecordedJson.ResolveExpression(BodyJson, "KeyConditionExpression");

    /// <summary>Gets the <c>FilterExpression</c> with its placeholders resolved, in the notation of the remarks of this class: <c>Status = "Placed"</c>; <see langword="null"/> if the request has none.</summary>
    /// <value>The text, or <see langword="null"/>.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public string? FilterText => RecordedJson.ResolveExpression(BodyJson, "FilterExpression");

    /// <summary>Gets the <c>ProjectionExpression</c> with its names resolved, in the notation of the remarks of this class: <c>name, address.city</c>; <see langword="null"/> if the request has none.</summary>
    /// <value>The text, or <see langword="null"/>.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public string? ProjectionText => RecordedJson.ResolveExpression(BodyJson, "ProjectionExpression");

    /// <summary>Gets the actions of a <c>TransactWriteItems</c> or <c>TransactGetItems</c> in the order of the request, read anew on each access. Empty for any other request.</summary>
    /// <value>The steps.</value>
    /// <exception cref="JsonException">The body is not JSON, or there is none.</exception>
    public IReadOnlyList<RecordedTransactStep> TransactItems =>
        BodyJson.TryGetProperty("TransactItems"u8, out var items) ? [.. items.EnumerateArray().Select(RecordedTransactStep.From)] : [];

    /// <summary>Reads the <c>Item</c> of the body, which a <c>PutItem</c> carries.</summary>
    /// <returns>A new item with the attributes as they went over the wire.</returns>
    /// <exception cref="InvalidOperationException">The body has no <c>Item</c>.</exception>
    public Item ReadItem() => ReadItem<Item>("Item");

    /// <summary>Reads the <c>Item</c> of the body the way a response that carries it is read, so that a test can compare what a <c>PutItem</c> carried with an entity.</summary>
    /// <typeparam name="T">The type to read: a generated entity or <see cref="Item"/>.</typeparam>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The body has no <c>Item</c>.</exception>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted, or the item is of another type.</exception>
    public T ReadItem<T>()
        where T : IDynamoDbDocument<T> => ReadItem<T>("Item");

    /// <summary>Reads any member of the body that has the form of an item, for example <c>ExclusiveStartKey</c>.</summary>
    /// <param name="member">The name of the member at the top of the body.</param>
    /// <returns>A new item with the attributes as they went over the wire.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The body has no such member.</exception>
    public Item ReadItem(string member) => ReadItem<Item>(member);

    /// <summary>Reads any member of the body that has the form of an item the way a response that carries it is read.</summary>
    /// <typeparam name="T">The type to read: a generated entity or <see cref="Item"/>.</typeparam>
    /// <param name="member">The name of the member at the top of the body, for example <c>Item</c>.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="member"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The body has no such member.</exception>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted, or the item is of another type.</exception>
    public T ReadItem<T>(string member)
        where T : IDynamoDbDocument<T>
    {
        ArgumentNullException.ThrowIfNull(member);
        return RecordedJson.ReadDocument<T>(BodyJson, member, $"{Operation ?? "recorded"} request");
    }

    /// <summary>
    /// Reads the <c>Key</c> of the body, which a <c>GetItem</c>, <c>UpdateItem</c> or <c>DeleteItem</c> carries. <see cref="StubResponse.KeyOf(IKey)"/> gives what to compare it with.
    /// </summary>
    /// <returns>A new item with the key attributes.</returns>
    /// <exception cref="InvalidOperationException">The body has no <c>Key</c>.</exception>
    public Item ReadKey() => ReadItem<Item>("Key");

    /// <summary>
    /// Reads the <c>Key</c> of the body through a type. A key holds the key attributes and nothing else, so an entity with other required attributes fails with a
    /// <see cref="DynamoDbMappingException"/>; for such a type, compare <see cref="ReadKey()"/> with <see cref="StubResponse.KeyOf(IKey)"/>.
    /// </summary>
    /// <typeparam name="T">The type to read, which can be made from the key attributes alone.</typeparam>
    /// <returns>The value.</returns>
    /// <exception cref="InvalidOperationException">The body has no <c>Key</c>.</exception>
    /// <exception cref="DynamoDbMappingException">An attribute is missing or cannot be converted.</exception>
    public T ReadKey<T>()
        where T : IDynamoDbDocument<T> => ReadItem<T>("Key");

    internal static string? GetOperation(HttpRequestMessage request) =>
        request.Headers.NonValidated.TryGetValues(WireProtocol.TargetHeaderName, out var values) ? ParseOperation(values.ToString()) : null;

    internal static async Task<RecordedRequest> CreateAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = RecordedHeaders.Copy(request.Headers, request.Content);
        var body = request.Content is { } content ? await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false) : [];
        return new RecordedRequest(request.Method, request.RequestUri, headers, body);
    }

    private static string? ParseOperation(string? target) => target?[(target.LastIndexOf('.', StringComparison.Ordinal) + 1)..];
}
