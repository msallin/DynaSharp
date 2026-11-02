using System.Text.Json;

namespace DynaSharp.UnitTests.Mapping;

public sealed class DynamoDbMappingExceptionTests
{
    [Test]
    public async Task Constructor_Attribute_NamesTypeAndAttribute()
    {
        // Arrange
        var inner = new FormatException("the value");

        // Act
        var exception = new DynamoDbMappingException("Order", "total", inner);

        // Assert
        await Assert.That((exception.EntityType, exception.AttributeName, exception.InnerException, exception.Message)).IsEqualTo(("Order", (string?)"total", (Exception?)inner, "The attribute \"total\" cannot be read as part of Order."));
    }

    [Test]
    public async Task Constructor_NoAttribute_NamesTheType()
    {
        // Arrange
        const string type = "Order";

        // Act
        var exception = new DynamoDbMappingException(type, null);

        // Assert
        await Assert.That((exception.AttributeName, exception.Message)).IsEqualTo(((string?)null, "The item cannot be read as Order."));
    }

    [Test]
    public async Task Missing_Attribute_SaysSo()
    {
        // Arrange
        const string attribute = "total";

        // Act
        var exception = DynamoDbMappingException.Missing("Order", attribute);

        // Assert
        await Assert.That(exception.Message).IsEqualTo("The attribute \"total\" is missing, so the item cannot be read as Order.");
    }

    [Test]
    public async Task Missing_AttributeThatAKeyCouldRestore_NamesBoth()
    {
        // Arrange
        const string attribute = "appointmentKey";

        // Act
        var exception = DynamoDbMappingException.Missing("Appointment", attribute, "pk");

        // Assert
        await Assert.That((exception.EntityType, exception.AttributeName, exception.Message)).IsEqualTo(
            ("Appointment", (string?)attribute, "The attribute \"appointmentKey\" is missing, and the key \"pk\" that could restore it is missing or does not fit its template, so the item cannot be read as Appointment."));
    }

    [Test]
    public async Task OtherType_Discriminator_NamesTheTypeFound()
    {
        // Arrange
        const string found = "INVOICE";

        // Act
        var exception = DynamoDbMappingException.OtherType("Order", "_type", found);

        // Assert
        await Assert.That((exception.EntityType, exception.AttributeName, exception.Message)).IsEqualTo(("Order", (string?)"_type", "The item is of type \"INVOICE\", so it cannot be read as Order."));
    }

    [Test]
    public async Task UndefinedEnumValue_Enum_NamesItAndNoValue()
    {
        // Arrange
        const string type = "Shop.Status";

        // Act
        var exception = DynamoDbMappingException.UndefinedEnumValue(type);

        // Assert
        await Assert.That((exception.EntityType, exception.AttributeName, exception.Message)).IsEqualTo((type, (string?)null, "The value is not a member of Shop.Status, so it has no name to store."));
    }

    [Test]
    public async Task IsConversionFailure_WhatConvertersThrow_IsTrue()
    {
        // Arrange
        Exception[] failures = [new JsonException(), new FormatException(), new OverflowException(), new InvalidOperationException()];

        // Act
        var results = failures.Select(DynamoDbMappingException.IsConversionFailure);

        // Assert
        await Assert.That(results.All(result => result)).IsTrue();
    }

    [Test]
    public async Task IsConversionFailure_AnythingElse_IsFalse()
    {
        // Arrange
        var failure = new ArgumentException("no");

        // Act
        var result = DynamoDbMappingException.IsConversionFailure(failure);

        // Assert
        await Assert.That(result).IsFalse();
    }
}
