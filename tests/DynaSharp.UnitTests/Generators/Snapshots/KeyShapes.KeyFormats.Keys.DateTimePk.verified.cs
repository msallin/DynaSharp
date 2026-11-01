
        /// <summary>The values of a parsed <c>datetimepk</c>.</summary>
        /// <param name="Utc">The value of the placeholder <c>Utc</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct DateTimePkParts(global::System.DateTime Utc);

        /// <summary>Builds and parses the partition key <c>datetimepk</c> of <c>KeyFormats</c>, with the template <c>T#{Utc}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class DateTimePk
        {
            private const int BufferLength = 26;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(global::System.DateTime utc)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, utc);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(global::System.DateTime utc) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(utc), "datetimepk");

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DateTimePkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static DateTimePkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out DateTimePkParts parts)
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
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out DateTimePkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("T#"u8)
                    && __key.TryReadTimestamp(global::DynaSharp.Keys.KeyTimeFormat.Millisecond, out global::System.DateTime __p0)
                    && __key.IsAtEnd)
                {
                    parts = new DateTimePkParts(__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, global::System.DateTime utc)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, utc);
                    __writer.WriteString(__key.WrittenSpan);
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template T#{Utc}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, global::System.DateTime utc)
            {
                __key.WriteLiteral("T#"u8);
                __key.WriteTimestamp(utc, global::DynaSharp.Keys.KeyTimeFormat.Millisecond, "Utc");
            }
        }
