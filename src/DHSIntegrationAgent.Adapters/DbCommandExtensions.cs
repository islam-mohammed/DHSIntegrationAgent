using System;
using System.Data.Common;

namespace DHSIntegrationAgent.Adapters;

internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand cmd, string parameterName, object? value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(parameter);
    }
}