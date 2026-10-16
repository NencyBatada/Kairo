export interface paths {
    "/api/auth/sign-in": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    returnUrl?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/auth/sign-out": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MeResponse"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/connections/callback/google": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    code?: string;
                    state?: string;
                    error?: string;
                };
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/members": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemberResponse"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/members/{userId}/roles": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ChangeRolesRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MemberResponse"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/members/{userId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    userId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PendingInvitation"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["InviteRequest"];
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["PendingInvitation"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/invitations/{invitationId}/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    invitationId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/me/invitations": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["MyInvitation"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/me/invitations/{invitationId}/accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    invitationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["JoinedWorkspace"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/me/invitations/{invitationId}/decline": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    invitationId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/approval-settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ApprovalSettings"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SaveApprovalSettingsRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ApprovalSettings"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["WorkspaceSummary"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path?: never;
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CreateWorkspaceRequest"];
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CreatedWorkspace"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["WorkspaceDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["UpdateWorkspaceRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["WorkspaceDetails"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        trace?: never;
    };
    "/api/brands/{brandId}/connections": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ConnectionView"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/connections/{product}/connect": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    product: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/connections/{product}/accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    product: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProductAccount"][];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/connections/{product}/account": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    product: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ChooseAccountRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ConnectionView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/connections/{product}/check": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    product: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckResult"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/connections/{product}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    product: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BrandKitView"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/draft": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SaveDraftRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitVersionView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/draft/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RevisionRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitVersionView"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/versions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitVersionSummary"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/versions/{number}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    number: number;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitVersionView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/versions/{number}/restore": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    number: number;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RevisionRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitVersionView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/files": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitFileView"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "multipart/form-data": {
                        file?: components["schemas"]["IFormFile"];
                    } & {
                        kind?: string;
                    };
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["KitFileView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/files/{fileId}/download": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/files/{fileId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    fileId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/drafting": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["DraftingRequest"];
                };
            };
            responses: {
                202: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DraftRunView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/brand-kit/drafting/latest": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["DraftRunView"];
                    };
                };
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/imports": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportView"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "multipart/form-data": {
                        file?: components["schemas"]["IFormFile"];
                    } & {
                        allowLargeRemoval?: boolean;
                    };
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/imports/{importId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    importId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportDetailView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/source": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SourceView"];
                    };
                };
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/summary": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CatalogueSummary"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/feed": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["FeedRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FeedSetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/feed/refresh": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ImportView"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/products": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    page?: number | string;
                    pageSize?: number | string;
                    search?: string;
                    category?: string;
                    availability?: string;
                    missing?: string;
                    status?: string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProductPage"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/products/{productId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    productId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProductDetailView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/catalogue/products/{productId}/fixes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    productId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["JsonObject"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProductView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    status?: string;
                    category?: string;
                    watched?: boolean;
                    minScore?: number | string;
                    triggered?: boolean;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OpportunityList"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/{opportunityId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    opportunityId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OpportunityDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/topics": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TopicView"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["TopicRequest"];
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TopicView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/topics/{topicId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    topicId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/{opportunityId}/accept": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    opportunityId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AcceptedView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/{opportunityId}/save": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    opportunityId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OpportunityDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/{opportunityId}/unsave": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    opportunityId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OpportunityDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/opportunities/{opportunityId}/dismiss": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    opportunityId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["DismissRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["OpportunityDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/options": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["TriggerOptions"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    status?: string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleView"][];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RuleRequest"];
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules/preview": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RuleRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RulePreview"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules/{ruleId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["RuleRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules/{ruleId}/pause": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules/{ruleId}/resume": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/rules/{ruleId}/activate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    ruleId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["RuleDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/festivals": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    from?: string;
                    to?: string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FestivalView"][];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["FestivalRequest"];
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FestivalView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/triggers/festivals/{festivalId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    festivalId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["FestivalRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FestivalView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    festivalId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/check-settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckSettingsView"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SaveCheckSettingsRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CheckSettingsView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/launch-plan": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LaunchPlanView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["LaunchPlanUpdate"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["LaunchPlanView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    Stage?: string;
                    Status?: string;
                    Owner?: string;
                    From?: string;
                    To?: string;
                    Q?: string;
                    Before?: string;
                    Limit?: number | string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignPage"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/activity": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    before?: string;
                };
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignActivityPage"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/approval-requests": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ApprovalRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/approve": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ApproveRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/send-back": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SendBackRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/discard": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["DiscardRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/stage-runs/{runId}/retry": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    runId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/proof-plan": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProofPlanResult"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/proof-plan/holdouts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["HoldoutRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/proof-plan/changes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ProofPlanChangeRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["ProofPlanResult"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/brief": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BriefDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BriefEditRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BriefDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/brief/instructions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["BriefInstructionRequestBody"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BriefProposalView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/brief/proposals/{proposalId}/apply": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    proposalId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["ApplyProposalRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["BriefDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    language?: string;
                };
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetSetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetEditRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/lock": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetVersionRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/unlock": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetVersionRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/regenerate": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetRegenerateRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetRegenerationView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/file": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    version?: number | string;
                };
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/history": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetHistoryView"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/skip-video": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/send-back": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetSendBackRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/check": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["AssetCheckRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["CampaignDetail"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/campaigns/{campaignId}/assets/{assetId}/override": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    campaignId: string;
                    assetId: string;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["CheckOverrideRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AssetView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/analytics/feeds": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["FeedsView"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/analytics/sales/uploads": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SalesUploadView"][];
                    };
                };
            };
        };
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "multipart/form-data": {
                        file?: components["schemas"]["IFormFile"];
                    };
                };
            };
            responses: {
                201: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SalesUploadView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/analytics/sales/source": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SalesSourceView"];
                    };
                };
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SalesSourceRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["SalesSourceView"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/notifications": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    before?: number | string;
                    limit?: number | string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["NotificationPage"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/notifications/{id}/read": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    id: number;
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
                404: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/notifications/read-all": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        post: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                204: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content?: never;
                };
            };
        };
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/notifications/email-settings": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["EmailSettings"];
                    };
                };
            };
        };
        put: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody: {
                content: {
                    "application/json": components["schemas"]["SaveEmailSettingsRequest"];
                };
            };
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["EmailSettings"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/live": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/event-stream": components["schemas"]["SseItemOfstring"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/audit-log": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    action?: string[];
                    actor?: string;
                    actorType?: string;
                    from?: string;
                    to?: string;
                    before?: string;
                    limit?: number | string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AuditLogPage"];
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/audit-log/export": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: {
                    action?: string[];
                    actor?: string;
                    actorType?: string;
                    from?: string;
                    to?: string;
                };
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "text/csv": string;
                    };
                };
                400: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/problem+json": components["schemas"]["HttpValidationProblemDetails"];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/api/brands/{brandId}/audit-log/actions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get: {
            parameters: {
                query?: never;
                header?: never;
                path: {
                    brandId: string;
                };
                cookie?: never;
            };
            requestBody?: never;
            responses: {
                200: {
                    headers: {
                        [name: string]: unknown;
                    };
                    content: {
                        "application/json": components["schemas"]["AuditActionOption"][];
                    };
                };
            };
        };
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        AcceptedView: {
            roomId: string;
            opportunity: components["schemas"]["OpportunityDetail"];
        };
        AgentCallView: {
            route: string;
            digest: string;
            language: null | string;
        };
        ApplyProposalRequest: {
            version: null | number | string;
        };
        ApprovalRequest: {
            gate: null | string;
            version: null | number | string;
        };
        ApprovalSettings: {
            briefApprovers: string[];
            assetApprovers: string[];
            launchApprovers: string[];
            budgetOwners: string[];
            budgetMoveApprovers: string[];
            targetChangeApprovers: string[];
            googleSuggestionApprovers: string[];
            budgetLimit: null | number | string;
            currency: string;
        };
        ApproveRequest: {
            gate: null | string;
            version: null | number | string;
            comment: null | string;
        };
        AssetCheckRequest: {
            version: null | number | string;
        };
        AssetCheckView: {
            state: string;
            score: null | number | string;
            passed: null | boolean;
            settingsVersion: null | number | string;
            checkedAt: null | string;
            issues: components["schemas"]["CheckIssueView"][];
            overridden: null | components["schemas"]["CheckOverrideView"];
            fixAttempts: number | string;
            fixes: components["schemas"]["CheckFixView"][];
        };
        AssetContentView: {
            keywords: null | components["schemas"]["AssetKeywordView"][];
            headlines: null | string[];
            longHeadlines: null | string[];
            descriptions: null | string[];
            path1: null | string;
            path2: null | string;
            finalUrl: null | string;
            businessName: null | string;
            callToAction: null | string;
            title: null | string;
            currentTitle: null | string;
            offerId: null | string;
            script: null | components["schemas"]["AssetScriptLineView"][];
        };
        AssetDisclosureView: {
            rules: string;
            aiGenerated: boolean;
            syntheticMedia: boolean;
            watermark: null | string;
            platformLabels: components["schemas"]["PlatformLabelView"][];
            blocker: null | string;
        };
        AssetEditRequest: {
            version: null | number | string;
            keywords: null | components["schemas"]["AssetKeywordInput"][];
            headlines: null | string[];
            longHeadlines: null | string[];
            descriptions: null | string[];
            path1: null | string;
            path2: null | string;
            businessName: null | string;
            callToAction: null | string;
            title: null | string;
        };
        AssetGroupView: {
            campaignType: string;
            label: string;
            language: string;
            languageName: null | string;
            assets: components["schemas"]["AssetView"][];
        };
        AssetHistoryVersionView: {
            version: number | string;
            origin: string;
            createdAt: string;
            createdBy: null | components["schemas"]["CampaignPersonView"];
            instruction: null | string;
            stageRunId: null | string;
            model: null | string;
            promptVersion: null | string;
            agentCalls: components["schemas"]["AgentCallView"][];
            sources: components["schemas"]["AssetSourcesView"];
            brandKitVersion: null | number | string;
            briefVersion: number | string;
            previousVersion: null | number | string;
            changedFields: string[];
            approvedBy: null | components["schemas"]["CampaignPersonView"];
            approvedAt: null | string;
            memoryUses: string[];
            content: components["schemas"]["AssetContentView"];
            mediaUrl: null | string;
            disclosure: components["schemas"]["AssetDisclosureView"];
        };
        AssetHistoryView: {
            assetId: string;
            campaignId: string;
            kind: string;
            campaignType: string;
            language: string;
            status: string;
            locked: boolean;
            currentVersion: number | string;
            versions: components["schemas"]["AssetHistoryVersionView"][];
        };
        AssetInputView: {
            assetId: string;
            version: number | string;
            kind: string;
        };
        AssetKeywordInput: {
            text: null | string;
            match: null | string;
        };
        AssetKeywordView: {
            text: string;
            match: string;
        };
        AssetRegenerateRequest: {
            version: null | number | string;
            instruction: null | string;
        };
        AssetRegenerationView: {
            runId: string;
            asset: components["schemas"]["AssetView"];
        };
        AssetScriptLineView: {
            voiceover: string;
            caption: string;
            start: number | string;
            end: number | string;
        };
        AssetSendBackRequest: {
            version: null | number | string;
            comment: null | string;
            assetIds: null | string[];
        };
        AssetSetCheckView: {
            score: null | number | string;
            passed: number | string;
            flagged: number | string;
            overridden: number | string;
            unchecked: number | string;
            settingsVersion: number | string;
            passScore: number | string;
            fixAttempts: number | string;
            approveBlocker: null | string;
        };
        AssetSetView: {
            campaignId: string;
            stage: string;
            canEdit: boolean;
            editBlocker: null | string;
            languages: string[];
            groups: components["schemas"]["AssetGroupView"][];
            check: components["schemas"]["AssetSetCheckView"];
            canSendBack: boolean;
        };
        AssetSourcesView: {
            productIds: string[];
            photoUrls: string[];
            inputs: components["schemas"]["AssetInputView"][];
        };
        AssetVersionRequest: {
            version: null | number | string;
        };
        AssetView: {
            id: string;
            kind: string;
            campaignType: string;
            language: string;
            adGroup: null | string;
            format: null | string;
            width: null | number | string;
            height: null | number | string;
            durationSeconds: null | number | string;
            productIds: string[];
            locked: boolean;
            checkState: string;
            version: number | string;
            origin: string;
            editedByPerson: boolean;
            mediaUrl: null | string;
            content: components["schemas"]["AssetContentView"];
            disclosure: components["schemas"]["AssetDisclosureView"];
            updatedAt: string;
            check: components["schemas"]["AssetCheckView"];
            canOverride: boolean;
            canSendBack: boolean;
        };
        AuditActionOption: {
            action: string;
            label: string;
        };
        AuditActorType: "user" | "agent" | "system";
        AuditEntryResponse: {
            id: number | string;
            occurredAt: string;
            actorType: components["schemas"]["AuditActorType"];
            actorId: null | string;
            actorName: string;
            actorEmail: null | string;
            action: string;
            targetType: string;
            targetId: string;
            before: null | components["schemas"]["JsonNode"];
            after: null | components["schemas"]["JsonNode"];
        };
        AuditLogPage: {
            entries: components["schemas"]["AuditEntryResponse"][];
            nextBefore: null | string;
        };
        BrandKitView: {
            approved: null | components["schemas"]["KitVersionView"];
            draft: null | components["schemas"]["KitVersionView"];
        };
        BrandRole: "admin" | "marketer" | "approver";
        BriefBudgetView: {
            amount: number | string;
            currency: string;
        };
        BriefChangeView: {
            field: string;
            label: string;
            before: string;
            after: string;
            reason: string;
        };
        BriefChoiceView: {
            value: string;
            label: string;
        };
        BriefDetail: {
            campaignId: string;
            campaignVersion: number | string;
            editable: boolean;
            brief: null | components["schemas"]["SmartBriefView"];
            warnings: components["schemas"]["BriefWarningView"][];
            budgetLimit: null | components["schemas"]["BriefBudgetView"];
            options: components["schemas"]["BriefOptionsView"];
            history: components["schemas"]["BriefVersionView"][];
        };
        BriefEditRequest: {
            version: null | number | string;
            briefVersion: null | number | string;
            title: null | string;
            objective: null | string;
            kpi: null | string;
            campaignTypes: null | string[];
            budget: null | number | string;
            startDate: null | string;
            endDate: null | string;
            regions: null | string[];
            audiences: null | string[];
            keyMessages: null | string[];
            productIds: null | string[];
            landingPages: null | string[];
            languages: null | string[];
        };
        BriefEvidenceView: {
            label: string;
            value: string;
            source: null | string;
        };
        BriefInstructionRequestBody: {
            version: null | number | string;
            briefVersion: null | number | string;
            instruction: null | string;
        };
        BriefOptionsView: {
            regions: components["schemas"]["BriefRegionOptionView"][];
            products: components["schemas"]["BriefProductView"][];
            campaignTypes: components["schemas"]["BriefChoiceView"][];
            kpis: components["schemas"]["BriefChoiceView"][];
            languages: components["schemas"]["BriefChoiceView"][];
        };
        BriefProductView: {
            id: string;
            title: string;
            price: null | number | string;
            currency: null | string;
            link: null | string;
            inStock: boolean;
        };
        BriefProposalView: {
            id: string;
            briefVersion: number | string;
            instruction: string;
            summary: string;
            changes: components["schemas"]["BriefChangeView"][];
            preview: components["schemas"]["SmartBriefView"];
            warnings: components["schemas"]["BriefWarningView"][];
            expiresAt: string;
        };
        BriefReasonView: {
            field: string;
            reason: string;
            evidence: components["schemas"]["BriefEvidenceView"][];
        };
        BriefRegionOptionView: {
            id: null | string;
            name: string;
            lastMonthSearches: null | number | string;
        };
        BriefVersionView: {
            version: number | string;
            origin: string;
            by: null | components["schemas"]["CampaignPersonView"];
            instruction: null | string;
            createdAt: string;
        };
        BriefView: {
            title: string;
            goal: string;
            audience: string;
            keyMessage: string;
            offer: null | string;
            regions: string[];
            productIds: string[];
            channels: string[];
            timing: string;
            model: null | string;
            promptVersion: null | string;
            draftedAt: string;
        };
        BriefWarningView: {
            code: string;
            field: string;
            severity: string;
            message: string;
        };
        CampaignActionsView: {
            requestApproval: null | string;
            approve: null | string;
            sendBackTo: string[];
            discard: boolean;
            retryRun: null | string;
            requestApprovalBlocker: null | string;
            continueWithoutVideo: boolean;
            approveBlocker: null | string;
            requestCheck: boolean;
        };
        CampaignActivityPage: {
            entries: components["schemas"]["CampaignActivityView"][];
            nextBefore: null | string;
        };
        CampaignActivityView: {
            id: number | string;
            occurredAt: string;
            actorType: string;
            actorName: string;
            action: string;
            label: string;
            details: null | components["schemas"]["JsonElement"];
        };
        CampaignApprovalView: {
            gate: string;
            label: string;
            approvers: components["schemas"]["CampaignPersonView"][];
            requestedBy: null | components["schemas"]["CampaignPersonView"];
            requestedAt: string;
            route: string;
            routeReason: null | string;
        };
        CampaignBriefView: {
            version: number | string;
            origin: string;
            title: string;
            goal: string;
            audience: string;
            keyMessage: string;
            offer: null | string;
            regions: string[];
            productIds: string[];
            channels: string[];
            timing: string;
            model: null | string;
            promptVersion: null | string;
            createdAt: string;
        };
        CampaignCard: {
            id: string;
            title: string;
            stage: string;
            status: string;
            owner: components["schemas"]["CampaignPersonView"];
            pendingGate: null | string;
            products: components["schemas"]["CampaignProductView"][];
            openedAt: string;
            stageEnteredAt: string;
            updatedAt: string;
        };
        CampaignDetail: {
            id: string;
            title: string;
            stage: string;
            status: string;
            version: number | string;
            owner: components["schemas"]["CampaignPersonView"];
            opportunityId: string;
            openedAt: string;
            updatedAt: string;
            tracker: components["schemas"]["CampaignTrackerView"];
            pendingApproval: null | components["schemas"]["CampaignApprovalView"];
            lastSendBack: null | components["schemas"]["CampaignSendBackView"];
            discardReason: null | string;
            evidence: components["schemas"]["JsonElement"];
            brief: null | components["schemas"]["CampaignBriefView"];
            runs: components["schemas"]["CampaignRunView"][];
            actions: components["schemas"]["CampaignActionsView"];
        };
        CampaignPage: {
            campaigns: components["schemas"]["CampaignCard"][];
            nextBefore: null | string;
        };
        CampaignPersonView: {
            id: string;
            name: string;
        };
        CampaignProductView: {
            id: string;
            title: string;
        };
        CampaignRunView: {
            id: string;
            stage: string;
            agent: string;
            kind: string;
            status: string;
            attempts: number | string;
            queuedAt: string;
            startedAt: null | string;
            finishedAt: null | string;
            model: null | string;
            promptVersion: null | string;
            inputTokens: null | number | string;
            outputTokens: null | number | string;
            error: null | string;
        };
        CampaignSendBackView: {
            from: string;
            to: string;
            comment: string;
            by: components["schemas"]["CampaignPersonView"];
            at: string;
        };
        CampaignStageStepView: {
            stage: string;
            label: string;
            progress: string;
        };
        CampaignTrackerView: {
            stages: components["schemas"]["CampaignStageStepView"][];
            current: string;
            ownerKind: string;
            owner: string;
            nextAction: string;
            enteredAt: string;
        };
        CatalogueSummary: {
            products: number | string;
            active: number | string;
            inStock: number | string;
            outOfStock: number | string;
            missing: components["schemas"]["MissingCounts"];
            categories: components["schemas"]["CategoryCount"][];
            lastImport: null | components["schemas"]["ImportView"];
            source: null | components["schemas"]["SourceView"];
        };
        CategoryCount: {
            name: string;
            products: number | string;
        };
        ChangeRolesRequest: {
            roles: components["schemas"]["BrandRole"][];
        };
        CheckFixView: {
            fromVersion: number | string;
            toVersion: number | string;
            at: string;
            issuesFixed: components["schemas"]["CheckIssueView"][];
            before: components["schemas"]["AssetContentView"];
            beforeMediaUrl: null | string;
            after: components["schemas"]["AssetContentView"];
            afterMediaUrl: null | string;
        };
        CheckIssueView: {
            rule: string;
            ruleName: string;
            severity: string;
            message: string;
            field: null | string;
            index: null | number | string;
            at: null | number | string;
            evidence: null | string;
            overridable: boolean;
        };
        CheckOverrideRequest: {
            version: null | number | string;
            reason: null | string;
        };
        CheckOverrideView: {
            by: string;
            at: string;
            reason: string;
        };
        CheckResult: {
            connection: components["schemas"]["ConnectionView"];
            problem: null | string;
        };
        CheckRuleView: {
            id: string;
            name: string;
            description: string;
            appliesTo: string[];
            severity: string;
            defaultSeverity: string;
            adjustable: boolean;
            overridable: boolean;
        };
        CheckSettingsView: {
            version: number | string;
            rules: components["schemas"]["CheckRuleView"][];
            customRules: components["schemas"]["CustomCheckRuleView"][];
            passScore: number | string;
            fixAttempts: number | string;
            updatedBy: null | string;
            updatedAt: null | string;
        };
        ChooseAccountRequest: {
            externalId: null | string;
        };
        ColourRole: "primary" | "secondary" | "accent" | "background" | "text";
        CompetitorPolicy: "never" | "neutral";
        ConditionRequest: {
            kind: null | string;
            threshold: null | number | string;
            withinDays: null | number | string;
            eventId: null | string;
            daysBefore: null | number | string;
            minUnits: null | number | string;
        };
        ConditionView: {
            kind: null | string;
            threshold: null | number | string;
            withinDays: null | number | string;
            eventId: null | string;
            eventName: null | string;
            daysBefore: null | number | string;
            minUnits: null | number | string;
        };
        ConnectionState: "notConnected" | "choosingAccount" | "connected" | "needsReconnect";
        ConnectionView: {
            product: string;
            name: string;
            state: components["schemas"]["ConnectionState"];
            externalId: null | string;
            displayName: null | string;
            connectedBy: null | string;
            connectedByName: null | string;
            connectedAt: null | string;
            checkedAt: null | string;
            reconnectReason: null | string;
        };
        CreatedWorkspace: {
            id: string;
            handle: string;
        };
        CreateWorkspaceRequest: {
            name: string;
            handle: string;
            timeZone: string;
            currency: string;
        };
        CustomCheckRuleRequest: {
            id: null | string;
            text: null | string;
            appliesTo: null | string;
            severity: null | string;
        };
        CustomCheckRuleView: {
            id: string;
            text: string;
            appliesTo: string;
            severity: string;
        };
        DiscardRequest: {
            reason: null | string;
            version: null | number | string;
        };
        DismissRequest: {
            reason: null | string;
            note: null | string;
        };
        DraftingRequest: {
            websiteUrl: null | string;
            replaceDraft: boolean;
        };
        DraftRunAd: {
            id: string;
            headline: string;
        };
        DraftRunError: {
            code: string;
            message: string;
        };
        DraftRunPage: {
            url: string;
            title: string;
        };
        DraftRunStatus: "pending" | "running" | "succeeded" | "failed";
        DraftRunView: {
            id: string;
            status: components["schemas"]["DraftRunStatus"];
            websiteUrl: string;
            pages: components["schemas"]["DraftRunPage"][];
            ads: components["schemas"]["DraftRunAd"][];
            error: null | components["schemas"]["DraftRunError"];
            requestedBy: string;
            requestedAt: string;
            finishedAt: null | string;
            version: null | number | string;
        };
        EmailSetting: {
            kind: string;
            name: string;
            description: string;
            email: boolean;
            canTurnOff: boolean;
        };
        EmailSettingChange: {
            kind: string;
            email: boolean;
        };
        EmailSettings: {
            kinds: components["schemas"]["EmailSetting"][];
        };
        FeedRequest: {
            url: null | string;
        };
        FeedSetView: {
            source: components["schemas"]["SourceView"];
            import: components["schemas"]["ImportView"];
        };
        FeedStatusView: {
            feed: string;
            state: string;
            lastRunAt: null | string;
            lastSuccessAt: null | string;
            coveredThrough: null | string;
            lastError: null | string;
        };
        FeedsView: {
            dataset: string;
            feeds: components["schemas"]["FeedStatusView"][];
        };
        FestivalRequest: {
            name: null | string;
            startsOn: null | string;
            endsOn: null | string;
            regions: null | string[];
        };
        FestivalView: {
            id: string;
            name: string;
            startsOn: string;
            endsOn: string;
            regions: components["schemas"]["PlaceView"][];
            origin: string;
            rules: number | string;
        };
        FiringView: {
            id: string;
            regions: components["schemas"]["PlaceView"][];
            firedAt: string;
            cooldownUntil: string;
            conditionEndsAt: string;
            endedAt: null | string;
            summary: string;
            outcome: string;
            opportunityId: null | string;
            opportunityStatus: null | string;
        };
        FixesView: {
            title: null | string;
            price: null | number | string;
            currency: null | string;
            link: null | string;
            imageLink: null | string;
            category: null | string;
        };
        HoldoutRequest: {
            regionIds: null | string[];
            version: null | number | string;
        };
        HttpValidationProblemDetails: {
            type?: null | string;
            title?: null | string;
            status?: null | number | string;
            detail?: null | string;
            instance?: null | string;
            errors?: {
                [key: string]: string[];
            };
        };
        IFormFile: string;
        ImportDetailView: {
            id: string;
            kind: string;
            fileName: null | string;
            feedUrl: null | string;
            status: string;
            rows: number | string;
            valid: number | string;
            invalid: number | string;
            added: number | string;
            updated: number | string;
            removed: number | string;
            unchanged: number | string;
            errorCode: null | string;
            errorMessage: null | string;
            startedBy: null | string;
            startedAt: string;
            finishedAt: string;
            problems: components["schemas"]["ImportProblem"][];
        };
        ImportProblem: {
            row: number | string;
            attribute: string;
            message: string;
        };
        ImportView: {
            id: string;
            kind: string;
            fileName: null | string;
            feedUrl: null | string;
            status: string;
            rows: number | string;
            valid: number | string;
            invalid: number | string;
            added: number | string;
            updated: number | string;
            removed: number | string;
            unchanged: number | string;
            errorCode: null | string;
            errorMessage: null | string;
            startedBy: null | string;
            startedAt: string;
            finishedAt: string;
        };
        InviteRequest: {
            email: string;
            roles: components["schemas"]["BrandRole"][];
        };
        JoinedWorkspace: {
            id: string;
            handle: string;
        };
        JsonElement: unknown;
        JsonNode: unknown;
        JsonObject: Record<string, never>;
        KitCategory: {
            name?: string;
            sources?: components["schemas"]["KitSource"][];
        };
        KitColour: {
            hex?: string;
            name?: string;
            role?: components["schemas"]["ColourRole"];
            sources?: components["schemas"]["KitSource"][];
        };
        KitCompetitor: {
            name?: string;
            policy?: components["schemas"]["CompetitorPolicy"];
        };
        KitContent: {
            voice?: components["schemas"]["KitVoice"];
            colours?: components["schemas"]["KitColour"][];
            logo?: null | components["schemas"]["KitLogo"];
            keyMessages?: components["schemas"]["KitMessage"][];
            productCategories?: components["schemas"]["KitCategory"][];
            rules?: components["schemas"]["KitRules"];
            guidelineFileIds?: string[];
        };
        KitDisclaimer: {
            text?: string;
            when?: string;
        };
        KitFileKind: "guideline" | "logo";
        KitFileView: {
            id: string;
            kind: components["schemas"]["KitFileKind"];
            fileName: string;
            contentType: string;
            size: number | string;
            deleted: boolean;
            uploadedBy: string;
            uploadedAt: string;
        };
        KitLogo: {
            fileId?: null | string;
            sourceUrl?: null | string;
        };
        KitMessage: {
            text?: string;
            sources?: components["schemas"]["KitSource"][];
        };
        KitRules: {
            bannedWords?: string[];
            requiredDisclaimers?: components["schemas"]["KitDisclaimer"][];
            tone?: string[];
            competitors?: components["schemas"]["KitCompetitor"][];
        };
        KitSource: {
            kind: components["schemas"]["SourceKind"];
            ref: string;
        };
        KitVersionSummary: {
            number: number | string;
            status: components["schemas"]["VersionStatus"];
            origin: components["schemas"]["VersionOrigin"];
            restoredFrom: null | number | string;
            createdBy: string;
            createdAt: string;
            updatedBy: string;
            updatedAt: string;
            approvedBy: null | string;
            approvedAt: null | string;
        };
        KitVersionView: {
            number: number | string;
            status: components["schemas"]["VersionStatus"];
            origin: components["schemas"]["VersionOrigin"];
            restoredFrom: null | number | string;
            revision: number | string;
            content: components["schemas"]["KitContent"];
            files: components["schemas"]["KitFileView"][];
            createdBy: string;
            createdAt: string;
            updatedBy: string;
            updatedAt: string;
            approvedBy: null | string;
            approvedAt: null | string;
        };
        KitVoice: {
            summary?: string;
            traits?: string[];
            dos?: string[];
            donts?: string[];
            examples?: string[];
            sources?: components["schemas"]["KitSource"][];
        };
        LaunchAsset: {
            id: string;
            version: number | string;
            kind: string;
            campaignType: string;
            language: string;
            adGroup: null | string;
            format: null | string;
            productIds: string[];
            content: components["schemas"]["JsonElement"];
            checkState: string;
            check: components["schemas"]["JsonElement"];
            fileKey: null | string;
            contentType: null | string;
            bytes: number | string;
            syntheticMedia: boolean;
            disclosureBlocker: null | string;
        };
        LaunchChannel: {
            campaignType: string;
            budget: number | string;
            biddingStrategy: string;
            target?: null | number | string;
        };
        LaunchDailyBudget: {
            campaignType: string;
            total: number | string;
            dailyMicros: number | string;
        };
        LaunchLogo: {
            fileId: string;
            brandKitVersion: number | string;
            contentType: string;
            digest: string;
        };
        LaunchPlan: {
            roomId: string;
            brandId: string;
            title: string;
            briefVersion: number | string;
            proofPlanVersion: number | string;
            budget: number | string;
            currency: string;
            timeZone: string;
            start: string;
            end: string;
            languages: string[];
            regions: components["schemas"]["LaunchRegion"][];
            holdouts: components["schemas"]["LaunchRegion"][];
            googleAdsAccountId: null | string;
            merchantAccountId: null | string;
            youTubeChannelId: null | string;
            productIds: string[];
            landingPages: string[];
            settings: components["schemas"]["LaunchSettings"];
            assets: components["schemas"]["LaunchAsset"][];
            logo?: null | components["schemas"]["LaunchLogo"];
            executionVersion: string;
        };
        LaunchPlanUpdate: {
            version: number | string;
            settings: components["schemas"]["LaunchSettings"];
        };
        LaunchPlanView: {
            version: number | string;
            plan: null | components["schemas"]["LaunchPlan"];
            hash: null | string;
            blocker: null | string;
            dailyBudgets: components["schemas"]["LaunchDailyBudget"][];
        };
        LaunchRegion: {
            id: string;
            name: string;
        };
        LaunchSettings: {
            channels: components["schemas"]["LaunchChannel"][];
            audienceIds?: null | string[];
            salesCountry?: null | string;
            geoTargetIds?: null | {
                [key: string]: string;
            };
        };
        MatchedProductView: {
            id: string;
            offerId: string;
            title: string;
            category: null | string;
            link: null | string;
            imageLink: null | string;
            price: null | number | string;
            salePrice: null | number | string;
            currency: null | string;
        };
        MemberResponse: {
            userId: string;
            name: string;
            email: string;
            pictureUrl: null | string;
            roles: components["schemas"]["BrandRole"][];
            joinedAt: string;
        };
        MeResponse: {
            id: string;
            name: string;
            email: string;
            pictureUrl: null | string;
        };
        MissingCounts: {
            title: number | string;
            price: number | string;
            link: number | string;
            image: number | string;
            stock: number | string;
            any: number | string;
        };
        MyInvitation: {
            id: string;
            workspaceName: string;
            workspaceHandle: string;
            roles: components["schemas"]["BrandRole"][];
            invitedBy: string;
            expiresAt: string;
        };
        NotificationItem: {
            id: number | string;
            kind: string;
            title: string;
            body: string;
            link: string;
            createdAt: string;
            readAt: null | string;
        };
        NotificationPage: {
            items: components["schemas"]["NotificationItem"][];
            unreadCount: number | string;
            nextBefore: null | number | string;
        };
        OpportunityCard: {
            id: string;
            topic: string;
            status: string;
            source: string;
            score: number | string;
            growth: number | string;
            mom: null | number | string;
            yoy: null | number | string;
            lastMonthSearches: number | string;
            avgMonthlySearches: number | string;
            trend: components["schemas"]["TrendPointView"][];
            regions: components["schemas"]["RegionView"][];
            products: components["schemas"]["MatchedProductView"][];
            productCount: number | string;
            reason: null | string;
            watched: boolean;
            available: boolean;
            firstSeenAt: string;
            lastSeenAt: string;
            savedAt: null | string;
            trigger: null | components["schemas"]["TriggerView"];
        };
        OpportunityDetail: {
            id: string;
            topic: string;
            status: string;
            source: string;
            score: number | string;
            growth: number | string;
            mom: null | number | string;
            yoy: null | number | string;
            lastMonthSearches: number | string;
            avgMonthlySearches: number | string;
            trend: components["schemas"]["TrendPointView"][];
            regions: components["schemas"]["RegionView"][];
            products: components["schemas"]["MatchedProductView"][];
            reason: null | string;
            watched: boolean;
            available: boolean;
            firstSeenAt: string;
            lastSeenAt: string;
            savedAt: null | string;
            keywords: string[];
            categories: string[];
            reasons: components["schemas"]["ReasonView"][];
            reasonsStatus: string;
            scoreBreakdown: components["schemas"]["ScoreBreakdownView"];
            closedAt: null | string;
            dismissReason: null | string;
            dismissNote: null | string;
            roomId: null | string;
            model: null | string;
            promptVersion: null | string;
            trigger: null | components["schemas"]["TriggerView"];
            triggers: components["schemas"]["TriggerView"][];
            brief: null | components["schemas"]["BriefView"];
            briefStatus: null | string;
        };
        OpportunityList: {
            refreshedAt: null | string;
            lastOutcome: null | string;
            lastReason: null | string;
            items: components["schemas"]["OpportunityCard"][];
        };
        PendingInvitation: {
            id: string;
            email: string;
            roles: components["schemas"]["BrandRole"][];
            invitedBy: string;
            sentAt: string;
            expiresAt: string;
        };
        PlaceView: {
            id: string;
            name: string;
            kind: string;
            stateId: null | string;
        };
        PlatformLabelView: {
            platform: string;
            setting: string;
            reason: string;
            source: string;
        };
        ProductAccount: {
            id: string;
            name: string;
            managerId?: null | string;
        };
        ProductDetailView: {
            fixes: components["schemas"]["FixesView"];
            stockEvents: components["schemas"]["StockEventView"][];
            id?: string;
            offerId?: string;
            title?: null | string;
            description?: null | string;
            link?: null | string;
            imageLink?: null | string;
            additionalImageLinks?: string[];
            price?: null | number | string;
            salePrice?: null | number | string;
            currency?: null | string;
            availability?: null | string;
            category?: null | string;
            googleCategory?: null | string;
            brand?: null | string;
            gtin?: null | string;
            itemGroupId?: null | string;
            fixed?: string[];
            missing?: string[];
            status?: string;
            updatedAt?: string;
        };
        ProductPage: {
            items: components["schemas"]["ProductView"][];
            page: number | string;
            pageSize: number | string;
            total: number | string;
        };
        ProductView: {
            id: string;
            offerId: string;
            title: null | string;
            description: null | string;
            link: null | string;
            imageLink: null | string;
            additionalImageLinks: string[];
            price: null | number | string;
            salePrice: null | number | string;
            currency: null | string;
            availability: null | string;
            category: null | string;
            googleCategory: null | string;
            brand: null | string;
            gtin: null | string;
            itemGroupId: null | string;
            fixed: string[];
            missing: string[];
            status: string;
            updatedAt: string;
        };
        ProofCostView: {
            amount: number | string;
            currency: string;
        };
        ProofLiftView: {
            days: number | string;
            lift: null | number | string;
        };
        ProofPlanChangeRequest: {
            version: null | number | string;
            reason: null | string;
            holdoutRegionIds: null | string[];
            endDate: null | string;
        };
        ProofPlanResult: {
            plan: null | components["schemas"]["ProofPlanView"];
            updating: boolean;
            canChooseHoldouts: boolean;
            canChangeAfterLock: boolean;
        };
        ProofPlanView: {
            version: number | string;
            briefVersion: number | string;
            unit: string;
            kpi: string;
            insideArea: boolean;
            chosenByUser: boolean;
            testRegions: components["schemas"]["ProofRegionView"][];
            holdouts: components["schemas"]["ProofRegionView"][];
            candidates: components["schemas"]["ProofRegionView"][];
            window: components["schemas"]["ProofWindowView"];
            minDetectableLift: null | number | string;
            liftByWindow: components["schemas"]["ProofLiftView"][];
            holdoutShare: null | number | string;
            holdoutCost: null | components["schemas"]["ProofCostView"];
            dataDays: number | string;
            placeboWindows: number | string;
            warnings: components["schemas"]["ProofWarningView"][];
            lockedAt: null | string;
            changedAfterLock: boolean;
            approvedWithWarnings: boolean;
            changeReason: null | string;
            createdAt: string;
        };
        ProofRegionView: {
            id: string;
            name: string;
            state: null | string;
            latitude: null | number | string;
            longitude: null | number | string;
            inArea: boolean;
            matchScore: null | number | string;
            share: null | number | string;
        };
        ProofWarningView: {
            code: string;
            message: string;
        };
        ProofWindowView: {
            start: null | string;
            end: null | string;
            days: null | number | string;
        };
        ReasonSourceView: {
            title: string;
            url: string;
        };
        ReasonView: {
            text: string;
            sources: components["schemas"]["ReasonSourceView"][];
        };
        RegionView: {
            id: string;
            name: string;
            lastMonthSearches: number | string;
            growth: null | number | string;
            rising: boolean;
        };
        RevisionRequest: {
            revision: number | string;
        };
        RuleDetail: {
            rule: components["schemas"]["RuleView"];
            firings: components["schemas"]["FiringView"][];
        };
        RulePreview: {
            description: string;
        };
        RuleRequest: {
            name: null | string;
            source: null | string;
            condition: null | components["schemas"]["ConditionRequest"];
            regions: null | string[];
            category: null | string;
            cooldownHours: null | number | string;
        };
        RuleResults: {
            fired: number | string;
            created: number | string;
            joined: number | string;
            accepted: number | string;
            lift: null | number | string;
        };
        RuleView: {
            id: string;
            name: string;
            source: string;
            condition: components["schemas"]["ConditionView"];
            regions: components["schemas"]["PlaceView"][];
            category: string;
            cooldownHours: number | string;
            status: string;
            origin: string;
            evidence: null | string;
            description: string;
            lastFiredAt: null | string;
            lastCheckedAt: null | string;
            lastCheckOutcome: null | string;
            results: components["schemas"]["RuleResults"];
            createdAt: string;
            updatedAt: string;
        };
        SalesColumns: {
            date: string;
            region: string;
            orders: string;
            revenue: string;
        };
        SalesColumnsRequest: {
            date: null | string;
            region: null | string;
            orders: null | string;
            revenue: null | string;
        };
        SalesSourceRequest: {
            table: null | string;
            columns: null | components["schemas"]["SalesColumnsRequest"];
        };
        SalesSourceView: {
            table: string;
            columns: components["schemas"]["SalesColumns"];
            location: string;
            setBy: string;
            setAt: string;
        };
        SalesUploadView: {
            id: string;
            fileName: string;
            rows: number | string;
            firstDate: string;
            lastDate: string;
            days: number | string;
            uploadedBy: string;
            uploadedAt: string;
        };
        SaveApprovalSettingsRequest: {
            briefApprovers: string[];
            assetApprovers: string[];
            launchApprovers: string[];
            budgetOwners: string[];
            budgetMoveApprovers: string[];
            targetChangeApprovers: string[];
            googleSuggestionApprovers: string[];
            budgetLimit: null | number | string;
        };
        SaveCheckSettingsRequest: {
            version: null | number | string;
            rules: null | {
                [key: string]: string;
            };
            customRules: null | components["schemas"]["CustomCheckRuleRequest"][];
            passScore: null | number | string;
            fixAttempts: null | number | string;
        };
        SaveDraftRequest: {
            content: null | components["schemas"]["KitContent"];
            revision: number | string;
        };
        SaveEmailSettingsRequest: {
            kinds: null | components["schemas"]["EmailSettingChange"][];
        };
        ScoreBreakdownView: {
            total: number | string;
            growth: number | string;
            growthScore: number | string;
            growthWeight: number | string;
            lastMonthSearches: number | string;
            volumeScore: number | string;
            volumeWeight: number | string;
            fit: number | string;
            fitScore: number | string;
            fitWeight: number | string;
            fitReason: null | string;
            feedback: number | string;
            feedbackReasons: string[];
        };
        SendBackRequest: {
            to: null | string;
            comment: null | string;
            version: null | number | string;
        };
        SmartBriefView: {
            version: number | string;
            origin: string;
            title: string;
            objective: string;
            kpi: string;
            campaignTypes: string[];
            budget: null | components["schemas"]["BriefBudgetView"];
            startDate: null | string;
            endDate: null | string;
            regions: string[];
            audiences: string[];
            keyMessages: string[];
            products: components["schemas"]["BriefProductView"][];
            landingPages: string[];
            languages: string[];
            reasons: components["schemas"]["BriefReasonView"][];
            model: null | string;
            promptVersion: null | string;
            createdAt: string;
        };
        SourceKind: "page" | "ad";
        SourceView: {
            kind: string;
            feedUrl: null | string;
            setBy: string;
            setAt: string;
            lastCheckedAt: null | string;
            lastSucceededAt: null | string;
            failuresInRow: number | string;
        };
        SseItemOfstring: {
            data?: null | string;
            eventType?: null | string;
            eventId?: null | string;
            reconnectionInterval?: null | string;
        };
        StockEventView: {
            from: null | string;
            to: null | string;
            importId: string;
            at: string;
        };
        TopicRequest: {
            phrase: null | string;
            kind: null | string;
        };
        TopicView: {
            id: string;
            phrase: string;
            kind: string;
            createdAt: string;
        };
        TrendPointView: {
            month: string;
            searches: number | string;
        };
        TriggerOptions: {
            regions: components["schemas"]["PlaceView"][];
            categories: string[];
            weatherKinds: components["schemas"]["WeatherKindView"][];
            defaultWithinDays: number | string;
            maxWithinDays: number | string;
            defaultDaysBefore: number | string;
            maxDaysBefore: number | string;
            defaultCooldownHours: number | string;
            maxCooldownHours: number | string;
            maxRegions: number | string;
        };
        TriggerRegionView: {
            id: string;
            name: string;
        };
        TriggerView: {
            firingId: string;
            ruleId: string;
            ruleName: string;
            source: string;
            summary: string;
            regions: components["schemas"]["TriggerRegionView"][];
            firedAt: string;
            conditionEndsAt: string;
            ended: boolean;
        };
        UpdateWorkspaceRequest: {
            name: null | string;
            timeZone: null | string;
        };
        VersionOrigin: "manual" | "drafted" | "restored";
        VersionStatus: "draft" | "approved" | "superseded" | "discarded";
        WeatherKindView: {
            kind: string;
            unit: string;
            default: number | string;
            min: number | string;
            max: number | string;
        };
        WorkspaceDetails: {
            id: string;
            handle: string;
            name: string;
            timeZone: string;
            currency: string;
            roles: components["schemas"]["BrandRole"][];
        };
        WorkspaceSummary: {
            id: string;
            handle: string;
            name: string;
            roles: components["schemas"]["BrandRole"][];
        };
    };
    responses: never;
    parameters: never;
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export type operations = Record<string, never>;
