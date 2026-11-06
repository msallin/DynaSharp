
        /// <summary>The values of a parsed <c>longpk</c>.</summary>
        /// <param name="Long">The value of the placeholder <c>Long</c>.</param>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public readonly record struct LongPkParts(long Long);

        /// <summary>Builds and parses the partition key <c>longpk</c> of <c>KeyFormats</c>, with the template <c>I#{Long}</c>.</summary>
        [global::System.CodeDom.Compiler.GeneratedCode("DynaSharp.Generators", "0.1.0")]
        public static class LongPk
        {
            private const int BufferLength = 256;

            /// <summary>Formats the complete key.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static string Format(long @long)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, @long);
                    return __key.ToString();
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("I#{Long}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Formats the complete key as a key of <c>KeyFormats</c>, which a query of another entity does not take. <c>TryOf</c> says the same without an exception, for a value from outside.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">A value cannot be part of a key: it is empty, holds a delimiter, or does not fit its format.</exception>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats> Of(long @long) => new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(Format(@long), "longpk");

            /// <summary>Does what <c>Of</c> does and returns <see langword="null"/> for a value that <c>Of</c> throws <c>KeyFormatException</c> for, for <c>is { } key</c>. Nothing is thrown on the way, and it costs what <c>Of</c> costs.</summary>
            public static global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>? TryOf(long @long)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength], false);
                try
                {
                    Encode(ref __key, @long);
                    return __key.IsInvalid ? null : new global::DynaSharp.PartitionKey<global::KeyShapes.KeyFormats>(__key.ToString(), "longpk");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            /// <summary>Parses a key. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static LongPkParts Parse(string text) => TryParse(text, out var parts) ? parts : throw Invalid();

            /// <summary>Parses a key given as UTF-8. It succeeds exactly when <c>Format</c> of the result gives the text back.</summary>
            /// <exception cref="global::DynaSharp.KeyFormatException">The text is not a key of this template.</exception>
            public static LongPkParts Parse(global::System.ReadOnlySpan<byte> utf8Text) => TryParse(utf8Text, out var parts) ? parts : throw Invalid();

            /// <summary>Tries to parse a key. Text that is not well-formed is never a key.</summary>
            public static bool TryParse(string? text, out LongPkParts parts)
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
            public static bool TryParse(global::System.ReadOnlySpan<byte> utf8Text, out LongPkParts parts)
            {
                var __key = new global::DynaSharp.Keys.KeyReader(utf8Text);
                if (__key.TryReadLiteral("I#"u8)
                    && __key.TryReadInt64("#"u8, out long __p0)
                    && __key.IsAtEnd)
                {
                    parts = new LongPkParts((long)__p0);
                    return true;
                }

                parts = default;
                return false;
            }

            internal static void Write(global::DynaSharp.Serialization.DynamoDbWriter __writer, long @long)
            {
                var __key = new global::DynaSharp.Keys.KeyWriter(stackalloc byte[BufferLength]);
                try
                {
                    Encode(ref __key, @long);
                    __writer.WriteString(__key.WrittenSpan);
                }
                catch (global::DynaSharp.KeyFormatException __failure)
                {
                    throw __failure.InTemplate("I#{Long}", "#");
                }
                finally
                {
                    __key.Dispose();
                }
            }

            private static global::DynaSharp.KeyFormatException Invalid() => new global::DynaSharp.KeyFormatException("The text is not a key of the template I#{Long}.");

            private static void Encode(ref global::DynaSharp.Keys.KeyWriter __key, long @long)
            {
                __key.WriteLiteral("I#"u8);
                __key.WriteInteger((long)@long, "#"u8, "Long");
            }
        }
