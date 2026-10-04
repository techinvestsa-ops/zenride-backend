namespace Izigo.Application.Common.Exceptions;

/// <summary>
/// Raised when an OTP or notification SMS cannot be delivered by the configured gateway.
/// </summary>
public sealed class SmsUnavailableException : InvalidOperationException
{
    public SmsUnavailableException(string message, Exception? inner = null) : base(message, inner) { }
}
