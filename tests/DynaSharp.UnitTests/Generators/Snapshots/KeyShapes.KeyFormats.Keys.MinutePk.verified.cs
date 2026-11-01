
        /// <summary>The values of a parsed <c>minutepk</c>.</summary>
        /// <param name="At">The value of the placeholder <c>At</c>.</param>
        public readonly record struct MinutePkParts(global::System.DateTimeOffset At);

        /// <summary>Builds and parses the partition key <c>minutepk</c> of <c>KeyFormats</c>, with the template <c>T#{At:yyyy-MM-ddTHH:mm}</c>.</summary>
        public static class MinutePk
        {
            private const int BufferLength = 18;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(global::System.DateTimeOffset at)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, at);
                    return __key.ToString();
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(global::System.DateTimeOffset at) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(at), "minutepk");

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static MinutePkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static MinutePkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out MinutePkParts parts)
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
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out MinutePkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("T#"u8)
                    && __key.TryReadTimestamp(global::DynaSharp.Keys.KeyTimeFormat.Minute, out global::System.DateTimeOffset __p0)
                    && __key.IsAtEnd)
                {
                    parts = new MinutePkParts(__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, global::System.DateTimeOffset at)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, at);
                    __writer.WriteString(__key.WrittenSpan);
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template T#{At:yyyy-MM-ddTHH:mm}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, global::System.DateTimeOffset at)
            {
                __key.WriteLiteral("T#"u8);
                __key.WriteTimestamp(at, global::DynaSharp.Keys.KeyTimeFormat.Minute);
            }
        }
