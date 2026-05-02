using System.Collections;

namespace Acmeshd;

internal sealed class ArrayEnvironment : IReadOnlyDictionary<string, string?>
{
    private readonly EnvVar[] variables;

    public ArrayEnvironment(EnvVar[] variables) => this.variables = variables;

    public int Count => this.variables.Length;

    public IEnumerable<string> Keys
    {
        get
        {
            for (var i = 0; i < this.variables.Length; i++)
                yield return this.variables[i].Name;
        }
    }

    public IEnumerable<string?> Values
    {
        get
        {
            for (var i = 0; i < this.variables.Length; i++)
                yield return this.variables[i].Value;
        }
    }

    public string? this[string key] => this.TryGetValue(key, out var value)
        ? value
        : throw new KeyNotFoundException(key);

    public bool ContainsKey(string key)
    {
        for (var i = 0; i < this.variables.Length; i++)
        {
            if (this.variables[i].Name == key)
                return true;
        }

        return false;
    }

    public bool TryGetValue(string key, out string? value)
    {
        for (var i = 0; i < this.variables.Length; i++)
        {
            if (this.variables[i].Name == key)
            {
                value = this.variables[i].Value;

                return true;
            }
        }

        value = null;

        return false;
    }

    public IEnumerator<KeyValuePair<string, string?>> GetEnumerator()
    {
        for (var i = 0; i < this.variables.Length; i++)
            yield return new KeyValuePair<string, string?>(this.variables[i].Name, this.variables[i].Value);
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
