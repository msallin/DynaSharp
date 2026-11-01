namespace DynaSharp.UnitTests.Generators.Analyzers;

/// <summary>
/// Expressions with a known verdict. "Documented" ones are the examples of the DynamoDB Developer Guide and of botocore's examples-1.json.
/// "Accepted" and "Rejected" ones went to DynamoDB Local 3.3.2, whose messages tell a syntax error from a complaint about the data: an accepted
/// expression may still fail there, for example on an attribute that does not exist, but it passed the grammar.
/// </summary>
internal static class ExpressionCorpus
{
    public static readonly string[] DocumentedConditions =
    [
        "attribute_exists (#Pictures.#SideView)",
        "attribute_not_exists (Manufacturer)",
        "attribute_type (ProductReviews.FiveStar, :v_sub)",
        "begins_with (Pictures.FrontView, :v_sub)",
        "contains (Brand, :v_sub)",
        "contains (Color, :v_sub)",
        "size (Brand) <= :v_sub",
        "size(VideoClip) > :v_sub",
        "size (Color) < :v_sub",
        "size(ProductReviews.OneStar) > :v_sub",
        "isVisible = :visible",
        "attribute_not_exists(Id)",
        "(ProductCategory IN (:cat1, :cat2)) and (Price between :lo and :hi)",
        "Price > :limit",
        "attribute_not_exists(Price)",
        "attribute_exists(ProductReviews.OneStar)",
        "attribute_type(Color, :v_sub)",
        "begins_with(Pictures.FrontView, :v_sub)",
        "contains(Color, :v_sub)",
        "size(VideoClip) > :v_sub",
        "Artist = :a",
        "Artist = :v1",
    ];

    public static readonly string[] DocumentedUpdates =
    [
        "REMOVE a SET b = a, c = b",
        "SET ProductCategory = :c, Price = :p",
        "SET RelatedItems = :ri, ProductReviews = :pr",
        "SET RelatedItems[1] = :ri",
        "SET #pr.#5star[1] = :r5, #pr.#3star = :r3",
        "SET Price = Price - :p",
        "SET #ri = list_append(#ri, :vals)",
        "SET #ri = list_append(:vals, #ri)",
        "SET Price = if_not_exists(Price, :p)",
        "REMOVE Brand, InStock, QuantityOnHand",
        "REMOVE RelatedItems[1], RelatedItems[2]",
        "ADD QuantityOnHand :q",
        "ADD Color :c",
        "DELETE Color :p",
        "SET Price = Price - :p REMOVE InStock",
        "SET RelatedItems[1] = :newValue, Price = :newPrice",
        "SET Price = Price - :discount",
        "SET #Y = :y, #AT = :t",
    ];

    public static readonly string[] AcceptedConditions =
    [
        "a = :v", "a <> :v", "a < :v", "a <= :v", "a > :v", "a >= :v", "a=:v AND b=:w", "a<:v", "a<=:v", "a<>:v", "a>:v", "a>=:v",
        "a BETWEEN :v AND :w", "a between :v and :w", "a BeTwEeN :v aNd :w", "a BETWEEN(:v)AND(:w)", "(a) BETWEEN (:v) AND (:w)",
        "a BETWEEN :v AND :w AND b = :x", "size(a) BETWEEN :v AND size(b)", "a BETWEEN size(b) AND :v",
        "a IN (:v)", "a in (:v, :w)", "a IN(:v)", "a IN(:v,:w)", "a IN (b, :v)", "a IN (size(b), :v)", "(a) IN ((:v), (:w))", "a IN ((:v))", "(a) IN (:v)",
        "NOT a = :v", "not a = :v", "NOT NOT a = :v", "NOT NOT NOT a = :v", "NOT(a = :v)", "NOT (a = :v)", "NOT (NOT (a = :v))", "NOT contains(a, :v)",
        "NOT(a = :v) AND b = :w", "(a = :v)", "(a) = :v", "a = (:v)", "(a) = (b)", "(attribute_exists(a))", "(attribute_exists(a)) AND (b = :v)",
        "a = b", ":v = a", ":v = :w", "a = :v AND b = :w OR c = :x", "a = :v OR NOT b = :w", "a = :v AND NOT b = :w", "(a = :v) AND (b = :w)",
        "(a = :v AND b = :w) OR c = :x", "(a = :v) OR (b = :w) AND c = :x", "a = :v AND(b = :w)", "a = :v OR(b = :w)", "(a = :v)AND(b = :w)",
        "size(a) > :v", "size(a.b[0]) > :v", "size (a) > :v", "size( a ) > :v", "(size(a)) > :v", "size((a)) > :v", "size(:v) > :w", "size(a) = size(b)",
        "attribute_exists(a)", "attribute_exists((a))", "attribute_exists (a)", "attribute_not_exists(a.b)", "attribute_exists(#n)", "attribute_exists(#n.a[0])",
        "begins_with(a, :v)", "begins_with(a, b)", "begins_with(:v, a)", "begins_with((a), (:v))",
        "contains(a, :v)", "contains(:v, a)", "contains(a, b)", "contains(a, size(b))", "contains(a, (:v))", "contains((a), :v)", "contains(a.b[0], :v)",
        "a.b = :v", "a . b = :v", "a.b.c[0][1].d = :v", "a [0] = :v", "a[ 0 ] = :v", "a[0] . b = :v", "a[0][1] = :v", "a  .  b  [  0  ]  =  :v", "a_b = :v", "a1 = :v", "a0 = :v",
        "A = :v", "a.b_c = :v", "a.size = :v", "size = :v", "contains = :v", "#n = :v", "#n.#m = :v", "#n[0] = :v", "#n.a = :v", "a.#n = :v", "#n1_x = :v", "a = #n", "#a=:v",
        ":v=#a", "#a<>:v", "a = :v\n AND b = :w", "a = :v\r\n AND b = :w", "a\t=\t:v", " a = :v ", "a[12] = :v",
    ];

    public static readonly string[] RejectedConditions =
    [
        "size(a)", "NOT size(a)", "attribute_exists(a) = :v", "attribute_exists(a, b)", "attribute_exists()", "attribute_type(a)", "begins_with(a)", "contains(a)", "size(a, b) > :v",
        "a = attribute_exists(b)", "a IN (attribute_exists(b))", "contains(a, attribute_exists(b))", "contains(attribute_exists(b), a)",
        "ATTRIBUTE_EXISTS(a)", "Attribute_Exists(a)", "SIZE(a) > :v", "Size(a) > :v", "BEGINS_WITH(a, :v)", "list_append(a, b) = :v", "if_not_exists(a, :v) = :v", "unknown(a)", "unknown(a) = :v",
        "a.0 = :v", "a[:v] = :v", "a[-1] = :v", "a[b] = :v", "a[] = :v", "a[0 = :v", "a..b = :v", ".a = :v", "a. = :v", "a[ = :v", "a] = :v", "[0] = :v", "1a = :v", "5 = :v", "a = 5",
        "a = 'x'", "a = \"x\"", "a == :v", "a != :v", "a => :v", "a =< :v", "a < > :v", "a > = :v", "a < = :v", "a AND b", "a = :v AND", "AND a = :v", "OR a = :v", "a = :v OR", "a = :v a",
        "a :v", "a b", "a NOT IN (:v)", "a NOT BETWEEN :v AND :w", "a BETWEEN :v", "a BETWEEN :v OR :w", "a BETWEEN :v AND", "a BETWEEN AND :v", "a BETWEEN (:v AND :w)", "a IN ()", "a IN :v",
        "a IN (:v", "a IN (:v,)", "a IN (,:v)", "a IN :v, :w", "a = :v)", "(a = :v", "()", "(", ")", "a = ()", "(a = :v) = :w", "(a) AND b = :v", "a + :v = :w", "a - :v = :w", "a = :v + :w",
        "(a + :v) = :w", "größe = :v", "a = :v;", "a = :v -- x", "a = :v /* x */", "AND = :v", "BETWEEN = :v", "IN = :v", "NOT = :v", "OR = :v", "a.AND = :v", ":v.a = :w", ":v[0] = :w",
        "# = :v", ": = :v", "a = :", "a = #", "#n", ":v", "a", "a.b", "a = :v,", "a = :v, b = :w", "a = (:v, :w)", "$a = :v", "a$ = :v", "@a = :v", "a-b = :v", "a b = :v", "a = :v AND (b = :w",
        "true", "a IS NULL", "a < :v < :w", "a = :v = :w", "NOT (a)", "NOT a", "(a.b).c = :v", "(a)[0] = :v", "(a).b = :v", "size(a).b > :v", "size(a)[0] > :v", string.Empty, " ",
        "a\u0001= :v", "a\f= :v", "a\v= :v", "a = :v", "a\\b = :v",
    ];

    public static readonly string[] AcceptedUpdates =
    [
        "SET a = :v", "set a = :v", "Set a = :v", "SET a = :v, b = :w", "SET a = :v REMOVE b", "REMOVE a", "remove a", "REMOVE a, b", "REMOVE a[0]", "ADD a :v", "add a :v", "ADD a :v, b :w",
        "DELETE a :v", "delete a :v", "DELETE a :v, b :w", "SET a = :v REMOVE b ADD c :w DELETE d :x", "DELETE d :x ADD c :w REMOVE b SET a = :v", "SET a = b", "SET a = b + c", "SET a = b - c",
        "SET a = :v + :w", "SET a = (b + c)", "SET a = (b) + (c)", "SET a = (:v)", "SET a = if_not_exists(a, :v)", "SET a = if_not_exists(a, :v) + :w", "SET a = :w + if_not_exists(a, :v)",
        "SET a = (if_not_exists(a, :v) + :w)", "SET a = list_append(a, :v)", "SET a = list_append(:v, a)", "SET a = list_append(if_not_exists(a, :v), :w)", "SET a = if_not_exists(a, if_not_exists(b, :v))",
        "SET a = if_not_exists((a), :v)", "SET a = if_not_exists(a.b, :v)", "SET a = if_not_exists(a, (:v))", "SET a = list_append((a), (b))", "SET a = (if_not_exists(a, :v)) + :w", "SET a = (b), c = (d)",
        "SET a = (b + c), d = (e)", "SET a = (list_append(b, c))", "SET a = b + if_not_exists(c, :v)", "SET a = (b) - (:v)", "SET a = ((b) + (c))", "SET a=b+c", "SET a = b+:v", "SET a = a-b", "SET a=:v",
        "SET a = a - :v", "SET a = a-:v", "SET a.b = :v", "SET a[0] = :v", "SET a[0].b = :v", "SET #a = :v", "SET #a.#b = :v", "SET a = b.c[0]", "REMOVE a.b, c[0].d", "REMOVE #a.#b", "ADD a.b :v", "ADD a[0] :v",
        "DELETE a.b :v", "DELETE a[0] :v", "ADD #a :v", "ADD a :v REMOVE b", "SET a = :v ,b = :w", "SET a = :v,b = :w", "SET #a = :v ADD b :w", " SET a = :v", "SET a = :v ", "REMOVE a[0], a[1]",
        "SET a = :v REMOVE b, c ADD d :w, e :x",
    ];

    public static readonly string[] RejectedUpdates =
    [
        "SET a = :v SET b = :w", "REMOVE a REMOVE b", "ADD a :v ADD b :w", "DELETE a :v DELETE b :w", "SET a = :v REMOVE b SET c = :w", "SET a = b + c + d", "SET a = b - c + d", "SET a = (b + c) + d",
        "SET a = d + (b + c)", "SET a = list_append(a, b, c)", "SET a = list_append(a)", "SET a = if_not_exists(a)", "SET a = if_not_exists(a, b, c)", "SET a = size(b)", "SET a = attribute_exists(b)",
        "SET a = contains(b, :v)", "SET a = begins_with(b, :v)", "SET a = unknown(b)", "SET a = LIST_APPEND(a, :v)", "SET a = IF_NOT_EXISTS(a, :v)", "SET :v = a", "SET (a) = :v", "SET a =", "SET a", "SET",
        "SET a = :v,", "SET , a = :v", "SET a = :v, REMOVE b", "SET a = :v b = :w", "REMOVE", "REMOVE a,", "REMOVE :v", "REMOVE (a)", "ADD", "ADD a", "ADD a b", "ADD a :v :w", "ADD :v a", "ADD (a) :v",
        "ADD a (:v)", "ADD a :v + :w", "ADD a :v,", "DELETE", "DELETE a", "DELETE a b", "DELETE (a) :v", "DELETE a (:v)", "SET a = :v AND b = :w", "a = :v", "SET a += :v", "SET a = 5", "SET a = 'x'",
        "SET a = b * c", "SET a = -b", "SET a = +b", "SET a = :v REMOVE", "REMOVE a SET", "SET SET = :v", "SET a = :v DELETE", "SET a = (b).c", "SET a = if_not_exists(a, :v).b", "SET a = (b + c).d",
        "SET a = list_append(a + b, c)", "SET a = if_not_exists(a, b + c)", "REMOVE a REMOVE", "ADD a :v, REMOVE b", "SET a = :v,, b = :w", "SETa = :v", "REMOVEa", "SET a = :v REMOVEb", "SET a = :vREMOVE b",
        "ADDa :v", string.Empty,
    ];
}
