namespace Izigo.Application.Common.Exceptions;

/// <summary>
/// Raised when the server-side Google Maps proxy cannot serve a geo request
/// (missing API key, denied key, or upstream Places failure).
/// </summary>
public sealed class MapsUnavailableException : InvalidOperationException
{
    public MapsUnavailableException(string message) : base(message) { }
}
