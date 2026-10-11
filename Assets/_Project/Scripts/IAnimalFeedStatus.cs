using System;

public enum FeedReaction { Fed, AlreadyFull }

public interface IAnimalFeedStatus
{
    bool FedToday { get; }
    event Action<FeedReaction> FeedReacted;
}
