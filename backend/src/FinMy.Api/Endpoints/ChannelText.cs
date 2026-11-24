using FinMy.Domain.Common;

namespace FinMy.Api.Endpoints;

/// <summary>Texto do canal nas respostas REST (<c>createdVia</c>: <c>app</c>, <c>claude</c> ou <c>system</c>, 008 research R5).</summary>
internal static class ChannelText
{
    public static string Of(Channel channel) => channel.ToString().ToLowerInvariant();
}
