const BACKEND_URI = import.meta.env.PROD ? "https://chatgpt-api.reddesert-78306892.westeurope.azurecontainerapps.io" : "http://localhost:5090";

import { ChatAppResponse, ChatAppResponseOrError, ChatAppRequest } from "./models";

function getHeaders(): Record<string, string> {
    var headers: Record<string, string> = {
        "Content-Type": "application/json"
    };

    return headers;
}

export async function chatApi(request: ChatAppRequest): Promise<Response> {
    return await fetch(`${BACKEND_URI}/chat`, {
        method: "POST",
        headers: getHeaders(),
        body: JSON.stringify(request)
    });
}