using System.Text;

namespace Tinkwell.Firmwareless.Hosting.WasmHost.Services;

/// <summary>
/// Line-oriented parser for <c>services.tw</c> until the full .tw parser is available.
/// </summary>
public static class ServicesTwParser
{
    public static List<ServiceDefinition> Parse(string content)
    {
        var stripped = StripComments(content);
        var list = new List<ServiceDefinition>();
        var i = 0;
        while (i < stripped.Length)
        {
            SkipWs(stripped, ref i);
            if (i >= stripped.Length)
                break;

            if (!TryConsumeKeyword(stripped, "service", ref i))
            {
                // Skip stray tokens until next service or end
                i++;
                continue;
            }

            SkipWs(stripped, ref i);
            var fullName = ReadServiceName(stripped, ref i);
            SkipWs(stripped, ref i);
            if (i >= stripped.Length || stripped[i] != '{')
                throw new FormatException($"Expected '{{' after service name near index {i}");
            i++; // '{'
            var body = ReadBalancedBlock(stripped, ref i);
            list.Add(ParseServiceBody(fullName, body));
        }

        return list;
    }

    private static bool MatchesKeyword(ReadOnlySpan<char> s, int p, string keyword)
    {
        for (var k=0; k < keyword.Length; ++k)
        {
            if (char.ToLowerInvariant(s[p + k]) != keyword[k])
                return false;
        }

        var beforeOk = p == 0 || !IsIdPart(s[p - 1]);
        var after = p + keyword.Length;
        var afterOk = after >= s.Length || !IsIdPart(s[after]);
        return beforeOk && afterOk;
    }

    private static ServiceDefinition ParseServiceBody(string fullName, ReadOnlySpan<char> body)
    {
        var methods = new List<MethodDefinition>();
        var depends = new List<string>();
        string? family = null;
        string? description = null;
        string? module = null;
        string? loadStr = null;
        string? handlerTemplate = null;

        var i = 0;
        while (i < body.Length)
        {
            SkipWs(body, ref i);
            if (i >= body.Length)
                break;

            if (TryConsumeKeyword(body, "method", ref i))
            {
                SkipWs(body, ref i);
                var methodName = ReadIdentifierOrQuoted(body, ref i);
                SkipWs(body, ref i);
                string? handlerOverride = null;
                if (TryConsumeKeyword(body, "handler", ref i))
                {
                    SkipWs(body, ref i);
                    if (i >= body.Length || body[i] != '=')
                        throw new FormatException("Expected '=' after handler in method");
                    i++;
                    SkipWs(body, ref i);
                    handlerOverride = ReadQuotedString(body, ref i);
                }

                ExpectChar(body, ';', ref i);
                methods.Add(new MethodDefinition
                {
                    Name = methodName,
                    HandlerOverride = handlerOverride,
                });
                continue;
            }

            if (TryConsumeKeyword(body, "depends_on", ref i))
            {
                SkipWs(body, ref i);
                var dep = ReadIdentifierOrQuoted(body, ref i);
                SkipWs(body, ref i);
                if (i < body.Length && body[i] == ';')
                    i++;
                depends.Add(dep);
                continue;
            }

            // key = value property
            var key = ReadIdentifier(body, ref i);
            SkipWs(body, ref i);
            if (i >= body.Length || body[i] != '=')
                throw new FormatException($"Unexpected token in service body near '{key}'");
            i++;
            SkipWs(body, ref i);
            var val = ReadQuotedString(body, ref i);
            SkipWs(body, ref i);
            if (i < body.Length && body[i] == ';')
                i++;

            switch (key)
            {
                case "family":
                    family = val;
                    break;
                case "description":
                    description = val;
                    break;
                case "module":
                    module = val;
                    break;
                case "load":
                    loadStr = val;
                    break;
                case "handler":
                    handlerTemplate = val;
                    break;
                default:
                    break;
            }
        }

        var loadPolicy = string.Equals(loadStr, "on-demand", StringComparison.OrdinalIgnoreCase)
            ? ModuleLoadPolicy.OnDemand
            : ModuleLoadPolicy.Startup;

        return new ServiceDefinition
        {
            FullName = fullName,
            Family = family,
            Description = description,
            Module = module,
            LoadPolicy = loadPolicy,
            HandlerTemplate = handlerTemplate,
            DependsOn = depends,
            Methods = methods,
        };
    }

    private static string ReadServiceName(ReadOnlySpan<char> s, ref int i)
    {
        return ReadIdentifierOrQuoted(s, ref i);
    }

    private static string ReadBalancedBlock(ReadOnlySpan<char> s, ref int i)
    {
        var start = i;
        var depth = 1;
        while (i < s.Length && depth > 0)
        {
            var c = s[i];
            if (c == '"' || c == '\'')
            {
                SkipStringLiteral(s, c, ref i);
                continue;
            }

            if (c == '{')
                depth++;
            else if (c == '}')
                depth--;

            if (depth == 0)
            {
                var inner = s.Slice(start, i - start);
                i++;
                return inner.ToString();
            }

            i++;
        }

        throw new FormatException("Unclosed '{' in service block");
    }

    private static void SkipStringLiteral(ReadOnlySpan<char> s, char quote, ref int i)
    {
        i++;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                i += 2;
                continue;
            }

            if (c == quote)
            {
                i++;
                return;
            }

            i++;
        }

        throw new FormatException("Unterminated string literal");
    }

    private static string ReadIdentifierOrQuoted(ReadOnlySpan<char> s, ref int i)
    {
        if (i >= s.Length)
            throw new FormatException("Unexpected end of input where name expected");

        var c = s[i];
        if (c == '"' || c == '\'')
            return ReadQuotedString(s, ref i);

        return ReadIdentifier(s, ref i);
    }

    private static string ReadQuotedString(ReadOnlySpan<char> s, ref int i)
    {
        if (i >= s.Length)
            throw new FormatException("Expected quoted string");

        var quote = s[i];
        if (quote != '"' && quote != '\'')
        {
            // Unquoted value (for compatibility)
            return ReadIdentifier(s, ref i);
        }

        i++;
        var sb = new StringBuilder();
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '\\' && i + 1 < s.Length)
            {
                i++;
                sb.Append(s[i]);
                i++;
                continue;
            }

            if (c == quote)
            {
                i++;
                return sb.ToString();
            }

            sb.Append(c);
            i++;
        }

        throw new FormatException("Unterminated string literal");
    }

    private static string ReadIdentifier(ReadOnlySpan<char> s, ref int i)
    {
        var start = i;
        if (i >= s.Length || !IsIdStart(s[i]))
            throw new FormatException("Identifier expected");

        i++;
        while (i < s.Length && IsIdPart(s[i]))
            i++;

        return s.Slice(start, i - start).ToString();
    }

    private static bool IsIdStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static void ExpectChar(ReadOnlySpan<char> s, char expected, ref int i)
    {
        SkipWs(s, ref i);
        if (i >= s.Length || s[i] != expected)
            throw new FormatException($"Expected '{expected}'");
        i++;
    }

    private static bool TryConsumeKeyword(ReadOnlySpan<char> s, string keyword, ref int i)
    {
        SkipWs(s, ref i);
        if (i + keyword.Length > s.Length)
            return false;

        for (var k=0; k < keyword.Length; ++k)
        {
            if (char.ToLowerInvariant(s[i + k]) != keyword[k])
                return false;
        }

        var after = i + keyword.Length;
        if (after < s.Length && IsIdPart(s[after]))
            return false;

        i = after;
        return true;
    }

    private static void SkipWs(ReadOnlySpan<char> s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i]))
            i++;
    }

    private static string StripComments(string content)
    {
        var sb = new StringBuilder(content.Length);
        var i = 0;
        while (i < content.Length)
        {
            if (i + 1 < content.Length && content[i] == '/' && content[i + 1] == '/')
            {
                i += 2;
                while (i < content.Length && content[i] != '\n')
                    i++;
                continue;
            }

            if (i + 1 < content.Length && content[i] == '/' && content[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < content.Length)
                {
                    if (content[i] == '*' && content[i + 1] == '/')
                    {
                        i += 2;
                        break;
                    }

                    i++;
                }

                continue;
            }

            sb.Append(content[i]);
            i++;
        }

        return sb.ToString();
    }
}
