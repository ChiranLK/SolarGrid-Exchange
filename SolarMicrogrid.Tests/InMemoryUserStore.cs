/*
 * InMemoryUserStore.cs
 * -----------------------------------------------------------------------------
 * File        : InMemoryUserStore.cs
 * Author      : H.A.S MADUWANTHA
 * IT Number   : IT23472020
 * Description : Test helper that backs the mocked Users collection with an
 *               in-memory list. It evaluates the filter operators Member 1 code
 *               uses ($eq, $ne, $in, $and), applies $set/$unset updates, sorts,
 *               and enforces unique _id/email on insert, so account-state tests
 *               exercise real transitions without a MongoDB server.
 * Date        : 2026-09-29
 * -----------------------------------------------------------------------------
 */

using System.Net;
using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using SolarMicrogrid.API.Models.Entities;

namespace SolarMicrogrid.Tests;

internal sealed class InMemoryUserStore
{
    private readonly List<BsonDocument> _documents = [];

    public InMemoryUserStore(Mock<IMongoCollection<User>> users, params User[] seed)
    {
        // Seed the store and route every Users call used by Member 1 code to it.
        foreach (User user in seed)
        {
            _documents.Add(user.ToBsonDocument());
        }

        users
            .Setup(collection => collection.FindAsync<User>(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, User>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilterDefinition<User> filter, FindOptions<User, User>? options, CancellationToken _) =>
            {
                IEnumerable<BsonDocument> matches = Match(filter);
                if (options?.Sort is not null)
                {
                    LastSort = options.Sort.Render(Args()).AsBsonDocument;
                    matches = Sort(matches, LastSort);
                }

                if (options?.Skip is int skip)
                {
                    matches = matches.Skip(skip);
                }

                if (options?.Limit is int limit && limit > 0)
                {
                    matches = matches.Take(limit);
                }

                return MongoTestContext.Cursor(matches.Select(ToUser).ToList());
            });

        // IFindFluent.AnyAsync() issues a projected find returning BsonDocument; existence is all that matters.
        users
            .Setup(collection => collection.FindAsync<BsonDocument>(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<FindOptions<User, BsonDocument>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilterDefinition<User> filter, FindOptions<User, BsonDocument>? _, CancellationToken _) =>
                MongoTestContext.Cursor(Match(filter).Take(1).Select(document => document.DeepClone().AsBsonDocument).ToList()));

        users
            .Setup(collection => collection.CountDocumentsAsync(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<CountOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilterDefinition<User> filter, CountOptions? options, CancellationToken _) =>
            {
                long count = Match(filter).LongCount();
                return options?.Limit is long limit && limit > 0 ? Math.Min(count, limit) : count;
            });

        users
            .Setup(collection => collection.FindOneAndUpdateAsync<User>(
                It.IsAny<FilterDefinition<User>>(),
                It.IsAny<UpdateDefinition<User>>(),
                It.IsAny<FindOneAndUpdateOptions<User, User>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((FilterDefinition<User> filter, UpdateDefinition<User> update,
                FindOneAndUpdateOptions<User, User>? options, CancellationToken _) =>
            {
                BsonDocument? target = Match(filter).FirstOrDefault();
                if (target is null)
                {
                    return null!;
                }

                BsonDocument before = target.DeepClone().AsBsonDocument;
                Apply(target, update.Render(Args()).AsBsonDocument);
                UpdateCount++;
                return ToUser(options?.ReturnDocument == ReturnDocument.Before ? before : target);
            });

        users
            .Setup(collection => collection.InsertOneAsync(
                It.IsAny<User>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns((User user, InsertOneOptions _, CancellationToken _) =>
            {
                BsonDocument document = user.ToBsonDocument();
                bool duplicate = _documents.Any(existing =>
                    existing["_id"] == document["_id"] ||
                    string.Equals(existing["email"].AsString, document["email"].AsString, StringComparison.OrdinalIgnoreCase));
                if (duplicate)
                {
                    throw DuplicateKeyWriteException();
                }

                _documents.Add(document);
                return Task.CompletedTask;
            });
    }

    public int UpdateCount { get; private set; }

    public BsonDocument? LastSort { get; private set; }

    public IReadOnlyList<User> All => _documents.Select(ToUser).ToList();

    public User? Get(string nic)
    {
        // Returns the current stored state of one user, or null.
        BsonDocument? document = _documents.FirstOrDefault(item => item["_id"].AsString == nic);
        return document is null ? null : ToUser(document);
    }

    private IEnumerable<BsonDocument> Match(FilterDefinition<User> filter)
    {
        // Returns the stored documents that satisfy the rendered filter, in insertion order.
        BsonDocument rendered = filter.Render(Args());
        return _documents.Where(document => Matches(document, rendered)).ToList();
    }

    private static bool Matches(BsonDocument document, BsonDocument filter)
    {
        // Evaluates the small subset of MongoDB query operators used by Member 1 services.
        foreach (BsonElement element in filter)
        {
            if (element.Name == "$and")
            {
                if (!element.Value.AsBsonArray.All(part => Matches(document, part.AsBsonDocument)))
                {
                    return false;
                }

                continue;
            }

            BsonValue actual = document.GetValue(element.Name, BsonNull.Value);
            if (element.Value is BsonDocument condition && condition.ElementCount > 0 &&
                condition.GetElement(0).Name.StartsWith('$'))
            {
                foreach (BsonElement op in condition)
                {
                    bool ok = op.Name switch
                    {
                        "$eq" => actual == op.Value,
                        "$ne" => actual != op.Value,
                        "$in" => op.Value.AsBsonArray.Contains(actual),
                        _ => throw new NotSupportedException($"Operator {op.Name} is not supported by the test store.")
                    };
                    if (!ok)
                    {
                        return false;
                    }
                }
            }
            else if (actual != element.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<BsonDocument> Sort(IEnumerable<BsonDocument> documents, BsonDocument sort)
    {
        // Applies a multi-key ascending/descending sort; missing fields sort first like MongoDB.
        IOrderedEnumerable<BsonDocument>? ordered = null;
        foreach (BsonElement key in sort)
        {
            string field = key.Name;
            bool descending = key.Value.ToInt32() < 0;
            Func<BsonDocument, BsonValue> selector = document => document.GetValue(field, BsonNull.Value);
            ordered = ordered is null
                ? (descending ? documents.OrderByDescending(selector) : documents.OrderBy(selector))
                : (descending ? ordered.ThenByDescending(selector) : ordered.ThenBy(selector));
        }

        return ordered ?? documents;
    }

    private static void Apply(BsonDocument target, BsonDocument update)
    {
        // Applies $set and $unset exactly as MongoDB would for top-level fields.
        if (update.TryGetValue("$set", out BsonValue set))
        {
            foreach (BsonElement element in set.AsBsonDocument)
            {
                target[element.Name] = element.Value;
            }
        }

        if (update.TryGetValue("$unset", out BsonValue unset))
        {
            foreach (BsonElement element in unset.AsBsonDocument)
            {
                target.Remove(element.Name);
            }
        }
    }

    private static User ToUser(BsonDocument document)
    {
        // Deserialises a stored document back into the entity type.
        return BsonSerializer.Deserialize<User>(document);
    }

    private static RenderArgs<User> Args()
    {
        // Render arguments matching the API's registered User serializer.
        return new RenderArgs<User>(BsonSerializer.LookupSerializer<User>(), BsonSerializer.SerializerRegistry);
    }

    internal static MongoWriteException DuplicateKeyWriteException()
    {
        // WriteError has no public constructor, so build the driver's duplicate-key error reflectively.
        ConstructorInfo constructor = typeof(WriteError)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .Single();
        object?[] arguments = constructor.GetParameters().Select(parameter => parameter.ParameterType switch
        {
            Type type when type == typeof(ServerErrorCategory) => (object)ServerErrorCategory.DuplicateKey,
            Type type when type == typeof(int) => 11000,
            Type type when type == typeof(string) => "E11000 duplicate key error",
            _ => null
        }).ToArray();
        var writeError = (WriteError)constructor.Invoke(arguments);
        var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
        return new MongoWriteException(connectionId, writeError, writeConcernError: null, innerException: null);
    }
}
