using Microsoft.Extensions.Logging;

namespace OrderSphere.BuildingBlocks.EventBus.AzureServiceBus;

/// <summary>
/// Log methods shared by every Service Bus processor. These templates were previously duplicated
/// verbatim across 15 processors (the start/stop lines) and 10 processors (the dead-letter line),
/// which meant a wording or level change had to be made in every one of them.
///
/// The message id, event type and queue are not repeated as template arguments: they are already
/// on the record as structured fields, put there by <see cref="MessageProcessingScope"/>.
///
/// EventId range 1100-1199 (see docs/logging.md).
/// </summary>
public static partial class ProcessorLog
{
    [LoggerMessage(
        EventId = 1101,
        Level = LogLevel.Information,
        Message = "{processor} started, listening on queue '{queue}'.")]
    public static partial void ProcessorStarted(this ILogger logger, string processor, string queue);

    [LoggerMessage(
        EventId = 1102,
        Level = LogLevel.Information,
        Message = "{processor} stopped.")]
    public static partial void ProcessorStopped(this ILogger logger, string processor);

    /// <summary>
    /// A body that will not deserialize is a producer-side contract break. It stays at Error
    /// rather than Warning: the message is parked in the dead-letter queue and needs a human to
    /// decide whether it can be replayed or must be discarded.
    /// </summary>
    [LoggerMessage(
        EventId = 1103,
        Level = LogLevel.Error,
        Message = "Message could not be deserialized. Dead-lettering.")]
    public static partial void MessageUndeserializable(this ILogger logger);

    /// <summary>
    /// Same condition as <see cref="MessageUndeserializable"/>, for the call sites where
    /// deserialization threw rather than returning null. Separate EventId because the
    /// <c>[LoggerMessage]</c> generator rejects two methods sharing one (SYSLIB1006).
    /// </summary>
    [LoggerMessage(
        EventId = 1109,
        Level = LogLevel.Error,
        Message = "Message could not be deserialized. Dead-lettering.")]
    public static partial void MessageUndeserializable(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1104,
        Level = LogLevel.Debug,
        Message = "Message received.")]
    public static partial void MessageReceived(this ILogger logger);

    [LoggerMessage(
        EventId = 1105,
        Level = LogLevel.Information,
        Message = "Message processed.")]
    public static partial void MessageProcessed(this ILogger logger);

    [LoggerMessage(
        EventId = 1106,
        Level = LogLevel.Information,
        Message = "Duplicate message ignored; already processed.")]
    public static partial void DuplicateMessageIgnored(this ILogger logger);

    [LoggerMessage(
        EventId = 1107,
        Level = LogLevel.Error,
        Message = "Unhandled exception while processing message. Abandoning.")]
    public static partial void MessageProcessingFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 1108,
        Level = LogLevel.Error,
        Message = "Service Bus processor error on entity {entityPath}, source {errorSource}.")]
    public static partial void ProcessorError(
        this ILogger logger, Exception exception, string entityPath, string errorSource);
}
