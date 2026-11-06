
        /// <summary>The values of a parsed <c>custompk</c>.</summary>
        /// <param name="Address">The value of the placeholder <c>Address</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct CustomPkParts(global::System.Net.IPAddress Address);

        /// <summary>Builds and parses the partition key <c>custompk</c> of <c>KeyFormats</c>, with the template <c>C#{Address}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class CustomPk
        {
            private const int BufferLength = 256;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(global::System.Net.IPAddress address)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, address);
                    return __key.ToString();
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("C#{Address}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take. <c>TryOf</c> says the same without an exception, for a value from outside.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(global::System.Net.IPAddress address) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(address), "custompk");

            /// <summary>Does what <c>Of</c> does and returns <see langword="null"/> for a value that <c>Of</c> throws <c>KeyFormatException</c> for, for <c>is { } key</c>. Nothing is thrown on the way, and it costs what <c>Of</c> costs.</summary>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>? TryOf(global::System.Net.IPAddress address)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength], false);
                try
                {
                    Encode(ref __key, address);
                    return __key.IsInvalid ? null : new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(__key.ToString(), "custompk");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static CustomPkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static CustomPkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out CustomPkParts parts)
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
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out CustomPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("C#"u8)
                    && __key.TryReadParsable<global::System.Net.IPAddress>("#"u8, out var __p0)
                    && __key.IsAtEnd)
                {
                    parts = new CustomPkParts(__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, global::System.Net.IPAddress address)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, address);
                    __writer.WriteString(__key.WrittenSpan);
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("C#{Address}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template C#{Address}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, global::System.Net.IPAddress address)
            {
                __key.WriteLiteral("C#"u8);
                __key.WriteFormattable(address, "#"u8, "Address");
            }
        }
