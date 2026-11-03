using DynaSharp;
using static DynaSharp.Expr;

namespace Mistakes;

// One mistake per rule, as a user of the package would make them. This project is meant to fail: the target in the parent project reads what the
// compiler printed, because a rule that does not reach a consumer's build keeps no promise.
public static class Code
{
    private static readonly Table<Item> Shop = null!;

    public static void Dyna122() => _ = Ticket.Keys.ByCodePk.Of("a#b");

    public static async Task Dyna201()
    {
        var command = Shop.Update("a");
        await Task.Delay(1);
        await command.SendAsync();
    }

    public static async Task Dyna202()
    {
        await Shop.Query("a");
    }

    public static void Dyna203()
    {
        Shop.Update("a");
    }

    public static void Dyna204()
    {
        var query = Shop.Query("a");
        var first = query.Limit(1);
        var second = query.Limit(2);
    }

    public static void Dyna205() => Orders.Update(Order.Keys.Of("a")).Set(Order.Attrs.Id, "b").ToRequestJson();

    public static void Dyna206() => Orders.Put(new Order { Id = "a", Revision = 1 }).IfNotExists().ToRequestJson();

    public static void Dyna207() => _ = Orders.Index("Nope");

    public static IAsyncEnumerable<Shipment> Dyna208() => Shipments.Scan().Project("Weight").ItemsAsync();

    public static string Dyna209() => Shipments.Get("a").Select(Shipment.Attrs.Id).ToRequestJson();

    public static void Dyna210() => Shop.Update("a").If($"{"status"} = {"open"}");

    public static void Dyna211() => Shop.Update("a").If($"attribute_exists(#pk)");

    public static void Dyna212() => Shop.Update("a").If($"atribute_exists({Attr("pk")})");

    // The entity comes from the generator in the package.
    public static void Dyna213() => Orders.Scan().Project("Totl");

    public static void Dyna214() => Shop.Update("a").Remove("a..b");

    public static void Dyna215() => Invoices.Scan().Filter(Order.Attrs.Total.Gt(1m));

    public static void Dyna216() => Orders.Update("a").If($"contains({Order.Attrs.Total}, {1})");

    public static Task Dyna217(DynamoDbClient client) => client.RetryOnConflictAsync(ct => new ValueTask<int>(client.RetryOnConflictAsync(_ => new ValueTask<int>(1), ct)));

    public static async Task Dyna218(DynamoDbClient client)
    {
        var order = await Orders.Get(Order.Keys.Of("a")).Consistent().ItemAsync();
        await client.RetryOnConflictAsync(async ct => await Orders.Put(order!).SendAsync(ct));
    }

    private static readonly Table<Order> Orders = null!;

    private static readonly Table<Invoice> Invoices = null!;

    private static readonly Table<Shipment> Shipments = null!;
}
