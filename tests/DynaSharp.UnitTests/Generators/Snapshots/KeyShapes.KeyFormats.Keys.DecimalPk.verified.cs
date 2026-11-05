
        /// <summary>The values of a parsed <c>decimalpk</c>.</summary>
        /// <param name="Money">The value of the placeholder <c>Money</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct DecimalPkParts(decimal Money);

        /// <summary>Builds and parses the partition key <c>decimalpk</c> of <c>KeyFormats</c>, with the template <c>M#{Money}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class DecimalPk
        {
            private const int BufferLength = 256;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(decimal money)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, money);
                    return __key.ToString();
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("M#{Money}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take. <c>TryOf</c> says the same without an exception, for a value from outside.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(decimal money) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(money), "decimalpk");

            /// <summary>Does what <c>Of</c> does and returns <see langword="false"/> for a value that <c>Of</c> throws <c>KeyFormatException</c> for. Nothing is thrown on the way, and it costs what <c>Of</c> costs.</summary>
            public static bool TryOf(decimal money, out global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> key)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength], false);
                try
                {
                    Encode(ref __key, money);
                    if (__key.IsInvalid)
                    {
                        key = default;
                        return false;
                    }

                    key = new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(__key.ToString(), "decimalpk");
                    return true;
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Does what <c>TryOf(..., out key)</c> does and returns the key, or <see langword="null"/> for a value that <c>Of</c> throws <c>KeyFormatException</c> for, for <c>is { } key</c>. It costs what <c>Of</c> costs.</summary>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>? TryOf(decimal money) => TryOf(money, out var key) ? key : null;

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DecimalPkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DecimalPkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out DecimalPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    if (__key.TryWriteText(text))
                    {
                        return TryParse(__key.WrittenSpan, out parts);
                    }

                    parts = default;
                    return false;
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Tries to parse a key given as UTF-8.</summary>
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out DecimalPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("M#"u8)
                    && __key.TryReadDecimal("#"u8, out decimal __p0)
                    && __key.IsAtEnd)
                {
                    parts = new DecimalPkParts(__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, decimal money)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, money);
                    __writer.WriteString(__key.WrittenSpan);
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("M#{Money}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template M#{Money}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, decimal money)
            {
                __key.WriteLiteral("M#"u8);
                __key.WriteDecimal(money, "#"u8, "Money");
            }
        }
