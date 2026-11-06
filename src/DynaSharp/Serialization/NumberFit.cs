namespace DynaSharp.Serialization;

/// <summary>Whether DynamoDB stores a number, or the limit it is beyond, see <see cref="DynamoDbNumber.FitsDynamoDb"/>.</summary>
internal enum NumberFit
{
    /// <summary>DynamoDB stores it.</summary>
    Fits,

    /// <summary>More than 38 significant digits.</summary>
    TooManyDigits,

    /// <summary>A magnitude of 1E+126 or more.</summary>
    TooLarge,

    /// <summary>A magnitude below 1E-130 that is not zero.</summary>
    TooSmall,
}
