﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Security;
using Umbraco.Extensions;

namespace Umbraco.Cms.Api.Management.Security;

public class BackOfficeApplicationManager : OpenIdDictApplicationManagerBase, IBackOfficeApplicationManager
{
    private readonly IWebHostEnvironment _webHostEnvironment;
    private readonly IRuntimeState _runtimeState;
    private readonly Uri? _backOfficeHost;
    private readonly string _authorizeCallbackPathName;
    private readonly string _authorizeCallbackLogoutPathName;

    public BackOfficeApplicationManager(
        IOpenIddictApplicationManager applicationManager,
        IWebHostEnvironment webHostEnvironment,
        IOptions<SecuritySettings> securitySettings,
        IRuntimeState runtimeState)
        : base(applicationManager)
    {
        _webHostEnvironment = webHostEnvironment;
        _runtimeState = runtimeState;
        _backOfficeHost = securitySettings.Value.BackOfficeHost;
        _authorizeCallbackPathName = securitySettings.Value.AuthorizeCallbackPathName;
        _authorizeCallbackLogoutPathName = securitySettings.Value.AuthorizeCallbackLogoutPathName;
    }

    public async Task EnsureBackOfficeApplicationAsync(Uri backOfficeUrl, CancellationToken cancellationToken = default)
    {
        // Install is okay without this, because we do not need a token to install,
        // but upgrades do, so we need to execute for everything higher then or equal to upgrade.
        if (_runtimeState.Level < RuntimeLevel.Upgrade)
        {
            return;
        }

        if (backOfficeUrl.IsAbsoluteUri is false)
        {
            throw new ArgumentException($"Expected an absolute URL, got: {backOfficeUrl}", nameof(backOfficeUrl));
        }

        await CreateOrUpdate(
            BackofficeOpenIddictApplicationDescriptor(backOfficeUrl),
            cancellationToken);

        if (_webHostEnvironment.IsProduction())
        {
            await Delete(Constants.OAuthClientIds.Swagger, cancellationToken);
            await Delete(Constants.OAuthClientIds.Postman, cancellationToken);
        }
        else
        {
            await CreateOrUpdate(
                new OpenIddictApplicationDescriptor
                {
                    DisplayName = "Umbraco Swagger access",
                    ClientId = Constants.OAuthClientIds.Swagger,
                    RedirectUris =
                    {
                        CallbackUrlFor(backOfficeUrl, "/umbraco/swagger/oauth2-redirect.html")
                    },
                    ClientType = OpenIddictConstants.ClientTypes.Public,
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                        OpenIddictConstants.Permissions.ResponseTypes.Code
                    }
                },
                cancellationToken);

            await CreateOrUpdate(
                new OpenIddictApplicationDescriptor
                {
                    DisplayName = "Umbraco Postman access",
                    ClientId = Constants.OAuthClientIds.Postman,
                    RedirectUris =
                    {
                        new Uri("https://oauth.pstmn.io/v1/callback"), new Uri("https://oauth.pstmn.io/v1/browser-callback")
                    },
                    ClientType = OpenIddictConstants.ClientTypes.Public,
                    Permissions =
                    {
                        OpenIddictConstants.Permissions.Endpoints.Authorization,
                        OpenIddictConstants.Permissions.Endpoints.Token,
                        OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                        OpenIddictConstants.Permissions.ResponseTypes.Code
                    }
                },
                cancellationToken);
        }
    }

    public OpenIddictApplicationDescriptor BackofficeOpenIddictApplicationDescriptor(Uri backOfficeUrl)
    {
        Uri CallbackUrl(string path) => CallbackUrlFor(_backOfficeHost ?? backOfficeUrl, path);
        return new OpenIddictApplicationDescriptor
        {
            DisplayName = "Umbraco back-office access",
            ClientId = Constants.OAuthClientIds.BackOffice,
            RedirectUris =
            {
                CallbackUrl(_authorizeCallbackPathName),
            },
            ClientType = OpenIddictConstants.ClientTypes.Public,
            PostLogoutRedirectUris =
            {
                CallbackUrl(_authorizeCallbackPathName),
                CallbackUrl(_authorizeCallbackLogoutPathName),
            },
            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,
                OpenIddictConstants.Permissions.Endpoints.Logout,
                OpenIddictConstants.Permissions.Endpoints.Revocation,
                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.GrantTypes.RefreshToken,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
            },
        };
    }

    private static Uri CallbackUrlFor(Uri url, string relativePath) => new Uri($"{url.GetLeftPart(UriPartial.Authority)}/{relativePath.TrimStart(Constants.CharArrays.ForwardSlash)}");
}
