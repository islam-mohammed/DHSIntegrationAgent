using System;
using System.Data;
using System.Data.Common;

namespace DHSIntegrationAgent.Adapters;

internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand cmd, string parameterName, object? value, DbType? dbType = null, int? size = null)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = parameterName;
        parameter.Value = value ?? DBNull.Value;

        if (dbType.HasValue)
        {
            parameter.DbType = dbType.Value;
        }

        if (size.HasValue)
        {
            parameter.Size = size.Value;
        }

        cmd.Parameters.Add(parameter);
    }
}