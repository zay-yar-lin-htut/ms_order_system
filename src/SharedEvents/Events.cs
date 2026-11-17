namespace SharedEvents;

public record OrderCreatedEvent(
    Guid OrderId,
    string CustomerId,
    decimal TotalAmount
);

public record PaymentCompletedEvent(
    Guid OrderId,
    bool IsSuccess,
    string Message
);
