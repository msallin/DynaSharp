using System.Net;
using DynaSharp.Testing.Fake;

namespace DynaSharp.UnitTests.Testing.Fake;

public sealed class FakeAnswerTests
{
    [Test]
    public async Task ToStubResponse_Answer_CarriesStatusBodyAndRequestId()
    {
        // Arrange
        var answer = new FakeAnswer(HttpStatusCode.BadRequest, "{\"a\":1}"u8.ToArray(), "id-1");

        // Act
        var response = answer.ToStubResponse();

        // Assert
        await Assert.That((response.StatusCode, response.Body, response.Headers!["x-amzn-RequestId"])).IsEqualTo((HttpStatusCode.BadRequest, "{\"a\":1}", "id-1"));
    }
}
