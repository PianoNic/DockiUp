using System.Text;
using DockiUp.Application.Dtos;

namespace DockiUp.Application.Compose
{
    /// <summary>Turns a pasted `docker run ...` command into a one-service compose file. Flags it can't
    /// express are reported as warnings instead of being dropped silently.</summary>
    public static class DockerRunConverter
    {
        // Flags that never take a value; anything else unknown is assumed to take one (most docker run flags do).
        private static readonly HashSet<string> BooleanFlags =
        [
            "-d", "--detach", "-i", "--interactive", "-t", "--tty", "--rm", "--privileged", "--init", "--read-only",
            "-P", "--publish-all", "--no-healthcheck", "--oom-kill-disable", "--sig-proxy", "--disable-content-trust", "-q", "--quiet",
        ];

        // Short flags with a value, which docker also accepts glued on: -p8080:80.
        private static readonly HashSet<char> ShortValueFlags = ['p', 'v', 'e', 'l', 'h', 'u', 'w', 'm', 'c', 'a'];

        public static GeneratedComposeDto Convert(string command)
        {
            var tokens = Tokenize(command);
            var start = 0;
            if (start < tokens.Count && tokens[start] == "docker") start++;
            if (start < tokens.Count && tokens[start] == "container") start++;
            if (start < tokens.Count && tokens[start] == "run") start++;
            else if (start > 0 || tokens.Count == 0)
                throw new ArgumentException("Paste a `docker run ...` command.");

            var spec = new ComposeServiceSpec();
            var warnings = new List<string>();
            var i = start;
            for (; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token == "--") { i++; break; }
                if (!token.StartsWith('-') || token == "-") break; // the image

                // Bundled short booleans: -dit
                if (!token.StartsWith("--") && token.Length > 2 && token[1..].All(c => "dit".Contains(c)))
                {
                    foreach (var c in token[1..]) Apply(spec, warnings, "-" + c, null);
                    continue;
                }

                string flag;
                string? value = null;
                var eq = token.IndexOf('=');
                if (token.StartsWith("--") && eq > 0)
                {
                    flag = token[..eq];
                    value = token[(eq + 1)..];
                }
                else if (!token.StartsWith("--") && token.Length > 2 && ShortValueFlags.Contains(token[1]))
                {
                    flag = token[..2];
                    value = token[2..];
                }
                else
                {
                    flag = token;
                }

                if (value is null && !BooleanFlags.Contains(flag))
                {
                    if (i + 1 >= tokens.Count) throw new ArgumentException($"{flag} needs a value.");
                    value = tokens[++i];
                }
                Apply(spec, warnings, flag, value);
            }

            if (i >= tokens.Count)
                throw new ArgumentException("No image found in the command.");
            spec.Image = tokens[i];
            spec.Command.AddRange(tokens.Skip(i + 1));
            spec.Name = ComposeServiceSpec.ServiceNameFrom(spec.ContainerName ?? spec.Image);
            return new GeneratedComposeDto(spec.ToYaml(), [.. warnings]);
        }

        private static void Apply(ComposeServiceSpec spec, List<string> warnings, string flag, string? value)
        {
            switch (flag)
            {
                case "-p": case "--publish": spec.Ports.Add(value!); break;
                case "-v": case "--volume": spec.Volumes.Add(value!); break;
                case "-e": case "--env": spec.Environment.Add(value!); break;
                case "--env-file": spec.EnvFiles.Add(value!); break;
                case "-l": case "--label": spec.Labels.Add(value!); break;
                case "--name": spec.ContainerName = value; break;
                case "--restart": spec.Restart = value; break;
                case "--network": case "--net":
                    // Built-in modes (and container:<id>) are a network_mode, not a network to join.
                    if (value is "host" or "bridge" or "none" || value!.StartsWith("container:"))
                        spec.NetworkMode = value;
                    else
                        spec.Networks.Add(value);
                    break;
                case "-h": case "--hostname": spec.Hostname = value; break;
                case "-u": case "--user": spec.User = value; break;
                case "-w": case "--workdir": spec.WorkingDir = value; break;
                case "--entrypoint": spec.Entrypoint.AddRange(Tokenize(value!)); break;
                case "--privileged": spec.Privileged = true; break;
                case "-i": case "--interactive": spec.StdinOpen = true; break;
                case "-t": case "--tty": spec.Tty = true; break;
                case "-d": case "--detach": break; // compose always runs detached under DockiUp
                case "--rm": warnings.Add("--rm has no compose equivalent and was dropped (compose manages the container's lifecycle)."); break;
                default:
                    warnings.Add(value is null
                        ? $"{flag} is not supported and was not converted."
                        : $"{flag} {value} is not supported and was not converted.");
                    break;
            }
        }

        /// <summary>Splits a shell command line: single/double quotes, backslash escapes, and backslash or
        /// caret line continuations (bash and Windows cmd style).</summary>
        public static List<string> Tokenize(string command)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            var inToken = false;
            char? quote = null;
            for (var i = 0; i < command.Length; i++)
            {
                var c = command[i];
                if (quote == '\'')
                {
                    if (c == '\'') quote = null; else current.Append(c);
                    continue;
                }
                if (c == '\\' || (c == '^' && quote is null && NextIsNewline(command, i)) || (c == '`' && quote is null && NextIsNewline(command, i)))
                {
                    if (NextIsNewline(command, i))
                    {
                        i = SkipNewline(command, i);
                        continue;
                    }
                    if (c == '\\' && i + 1 < command.Length)
                    {
                        // Inside double quotes only these are escapes; elsewhere any char is.
                        var next = command[i + 1];
                        if (quote is null || next is '"' or '\\' or '$' or '`')
                        {
                            current.Append(next);
                            i++;
                            inToken = true;
                            continue;
                        }
                    }
                }
                if (quote == '"')
                {
                    if (c == '"') quote = null; else current.Append(c);
                    continue;
                }
                if (c is '\'' or '"')
                {
                    quote = c;
                    inToken = true;
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    if (inToken) tokens.Add(current.ToString());
                    current.Clear();
                    inToken = false;
                    continue;
                }
                current.Append(c);
                inToken = true;
            }
            if (quote is not null) throw new ArgumentException("The command has an unclosed quote.");
            if (inToken) tokens.Add(current.ToString());
            return tokens;
        }

        private static bool NextIsNewline(string s, int i)
        {
            var j = i + 1;
            while (j < s.Length && (s[j] == ' ' || s[j] == '\t')) j++;
            return j < s.Length && (s[j] == '\n' || s[j] == '\r');
        }

        private static int SkipNewline(string s, int i)
        {
            var j = i + 1;
            while (s[j] != '\n' && s[j] != '\r') j++;
            if (s[j] == '\r' && j + 1 < s.Length && s[j + 1] == '\n') j++;
            return j;
        }
    }
}
