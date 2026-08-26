using System.Text;

namespace SnapData.Migrations.Cli.Presentation;

internal sealed class AnsiStrippingTextWriter(TextWriter inner) : TextWriter
{
    private ParseState state;

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value)
    {
        lock (inner)
        {
            switch (state)
            {
                case ParseState.Text when value == '\u001b':
                    state = ParseState.Escape;
                    return;
                case ParseState.Escape when value == '[':
                    state = ParseState.ControlSequence;
                    return;
                case ParseState.Escape:
                    state = ParseState.Text;
                    return;
                case ParseState.ControlSequence when value is >= '@' and <= '~':
                    state = ParseState.Text;
                    return;
                case ParseState.ControlSequence:
                    return;
                default:
                    inner.Write(value);
                    return;
            }
        }
    }

    public override void Write(string? value)
    {
        if (value is null)
        {
            return;
        }
        Write(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        foreach (var value in buffer)
        {
            Write(value);
        }
    }

    public override void Flush() => inner.Flush();

    private enum ParseState
    {
        Text,
        Escape,
        ControlSequence
    }
}
