using System.Text.Json.Serialization;

namespace ProfitGraph.Api.Strategies.Contracts;

public sealed record NodePositionRequest([property: JsonRequired] double X, [property: JsonRequired] double Y);