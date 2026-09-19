using System.ClientModel;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace BankingApp.Agent;

/// <summary>
/// Builds the MAF agent. Adapted from the Session 2 scaffold's BankingAgent
/// Program.cs: OpenAI-SDK-v2 client pointed at the shared Agent Gateway LLM
/// endpoint (which serves at /v1/chat/completions, so the base carries an
/// explicit /v1 suffix), instructions loaded from SystemPrompt.md (kept as a
/// plain file so PromptDefenseCommand can read it), and tools bound from the
/// in-process MCP client.
/// </summary>
public static class BankingAgentFactory
{
    public static AIAgent Create(
        string gatewayEndpoint,
        string gatewayKey,
        string gatewayModel,
        string instructions,
        IList<AITool> tools)
    {
        var llmBaseUri = gatewayEndpoint.TrimEnd('/') + "/v1";
        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(gatewayKey),
            new OpenAIClientOptions { Endpoint = new Uri(llmBaseUri) });

        var chatClient = openAiClient.GetChatClient(gatewayModel);
        IChatClient iChatClient = chatClient.AsIChatClient();

        return iChatClient.AsAIAgent(
            name: "FDE-BankingAgent",
            instructions: instructions,
            tools: tools);
    }
}