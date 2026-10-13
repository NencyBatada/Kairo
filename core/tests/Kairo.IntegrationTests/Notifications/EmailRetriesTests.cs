using Kairo.Modules.Notifications.Delivery;

namespace Kairo.IntegrationTests.Notifications;

public sealed class EmailRetriesTests
{
    [Fact]
    public void Failed_attempts_wait_longer_each_time_and_the_sixth_gives_up()
    {
        var waits = Enumerable.Range(1, 6).Select(EmailRetries.After).ToArray();

        Assert.Equal(
            new TimeSpan?[]
            {
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMinutes(30),
                TimeSpan.FromHours(2),
                TimeSpan.FromHours(6),
                null,
            },
            waits);
        Assert.Equal(6, EmailRetries.MaxAttempts);
    }
}
