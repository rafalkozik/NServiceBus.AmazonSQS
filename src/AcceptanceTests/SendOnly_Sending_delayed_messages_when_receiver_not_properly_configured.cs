﻿namespace NServiceBus.AcceptanceTests
{
    using AcceptanceTesting;
    using AcceptanceTesting.Customization;
    using EndpointTemplates;
    using NServiceBus;
    using NUnit.Framework;
    using System;
    using System.Linq;
    using System.Threading.Tasks;

    public class SendOnly_Sending_delayed_messages_when_receiver_not_properly_configured : NServiceBusAcceptanceTest
    {
        [Test]
        public async Task Should_deliver_messages_only_if_below_threshold()
        {
            var payload = "some payload";
            var delay = TimeSpan.FromSeconds(2);

            var context = await Scenario.Define<Context>()
                .WithEndpoint<SendOnlySender>(b => b.When(async (session, c) =>
                {
                    var sendOptions = new SendOptions();
                    sendOptions.DelayDeliveryWith(delay);

                    await session.Send(new DelayedMessage
                    {
                        Payload = payload
                    }, sendOptions);

                    c.SentAt = DateTime.UtcNow;
                }))
                .WithEndpoint<NotConfiguredReceiver>()
                .Done(c => c.Received)
                .Run();

            Assert.GreaterOrEqual(context.ReceivedAt - context.SentAt, delay, "The message has been received earlier than expected, we're so good!");
            Assert.AreEqual(payload, context.Payload, "The received payload doesn't match the sent one. BAD BAD BAD");
        }

        [Test]
        public void Should_fail_to_deliver_messages_if_above_threshold()
        {
            var delay = TimeSpan.FromSeconds(2);

            /*
             * In this scenario sender knows nothing about receiver configuration
             * sender won't be able to find the "<receiver>-delays.fifo" queue
             * so I suppose that the SDK will throw a meaningful exception at send time
             */
            //or whatever exception we want to throw
            Assert.ThrowsAsync<NotSupportedException>(async () =>
            {
                var context = await Scenario.Define<Context>()
                .WithEndpoint<SendOnlySender>(b => b.When(async (session, c) =>
                {
                    var sendOptions = new SendOptions();
                    sendOptions.DelayDeliveryWith(delay);

                    await session.Send(new DelayedMessage
                    {
                        Payload = ""
                    }, sendOptions);

                    c.SentAt = DateTime.UtcNow;
                }))
                .Done(c => c.FailedMessages.Any())
                .Run();
            });
        }

        public class Context : ScenarioContext
        {
            public bool Received { get; set; }
            public string Payload { get; set; }
            public DateTime SentAt { get; set; }
            public DateTime ReceivedAt { get; set; }
        }


        public class SendOnlySender : EndpointConfigurationBuilder
        {
            public SendOnlySender()
            {
                EndpointSetup<DefaultServer>(builder =>
                {
                    builder.ConfigureTransport().Routing().RouteToEndpoint(typeof(DelayedMessage), typeof(NotConfiguredReceiver));
                    builder.SendOnly();

                    //TODO: chose the "NativeDelayedDeliveries" extension method name
                    //builder.ConfigureSqsTransport().NativeDelayedDeliveries();

                    //we should provide an internal overload for test purposes only so to tweak the defaul AWS delivery threshold that is 15 minutes
                    //builder.ConfigureSqsTransport().NativeDelayedDeliveries(testThreshold = 10 seconds);
                });
            }
        }

        public class NotConfiguredReceiver : EndpointConfigurationBuilder
        {
            public NotConfiguredReceiver()
            {
                EndpointSetup<DefaultServer>(builder =>
                {

                });
            }

            public class MyMessageHandler : IHandleMessages<DelayedMessage>
            {
                public Context Context { get; set; }

                public Task Handle(DelayedMessage message, IMessageHandlerContext context)
                {
                    Context.Received = true;
                    Context.Payload = message.Payload;
                    Context.ReceivedAt = DateTime.UtcNow;

                    return Task.FromResult(0);
                }
            }

        }

        public class DelayedMessage : IMessage
        {
            public string Payload { get; set; }
        }
    }
}
