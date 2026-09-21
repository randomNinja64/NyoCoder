using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace NyoCoder
{
public partial class LLMClient
{
    private JObject BuildMessageObject(ChatMessage msg)
    {
        JObject msgObj = new JObject();
        msgObj["role"] = msg.Role;

        if (!string.IsNullOrEmpty(msg.ToolCallId))
            msgObj["tool_call_id"] = msg.ToolCallId;

        if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
        {
            msgObj["content"] = msg.Content ?? "";
            JArray toolCallsArray = new JArray();

            foreach (var call in msg.ToolCalls)
            {
                JObject toolObj = new JObject();
                toolObj["id"] = call.Id ?? "";
                toolObj["type"] = "function";

                JObject functionObj = new JObject();
                functionObj["name"] = call.Name ?? "";
                functionObj["arguments"] = call.Arguments ?? "";

                toolObj["function"] = functionObj;
                toolCallsArray.Add(toolObj);
            }

            msgObj["tool_calls"] = toolCallsArray;
        }
        else if (msg.Image != null)
        {
            JArray contentArray = new JArray();

            if (!string.IsNullOrEmpty(msg.Content))
            {
                JObject textPart = new JObject();
                textPart["type"] = "text";
                textPart["text"] = msg.Content;
                contentArray.Add(textPart);
            }

            if (!string.IsNullOrEmpty(msg.Image))
            {
                JObject imgPart = new JObject();
                imgPart["type"] = "image_url";
                JObject imageUrl = new JObject();
                string mime = string.IsNullOrEmpty(msg.ImageMime) ? "image/png" : msg.ImageMime;
                imageUrl["url"] = "data:" + mime + ";base64," + msg.Image;
                imgPart["image_url"] = imageUrl;
                contentArray.Add(imgPart);
            }

            if (contentArray.Count == 0)
            {
                JObject emptyText = new JObject();
                emptyText["type"] = "text";
                emptyText["text"] = "";
                contentArray.Add(emptyText);
            }

            msgObj["content"] = contentArray;
        }
        else
        {
            msgObj["content"] = msg.Content ?? "";
        }

        return msgObj;
    }

    LLMCompletionResponse sendMessages(
        List<ChatMessage> conversation,
        Action<string> outputCallback = null,
        Action<ToolHandler.ToolCall> toolCallCallback = null,
        Func<bool> stopRequested = null,
        string modeId = ModeIds.Agent,
        Action startBlock = null,
        bool includeTools = true,
        bool includeContextInjections = true)
    {
        // Build payload
        JObject payload = new JObject();
        payload["model"] = model;

        // Messages
        JArray messages = new JArray();

        // System message — includes mode-specific instructions and injected tool context
        string systemPrompt = ContextEngine.GetSystemPrompt(modeId);
        if (includeContextInjections)
        {
            List<string> enabledTools = ToolDefinitions.GetEnabledToolNames(modeId);
            if (SkillHandler.AnySkillToolEnabled(enabledTools))
                systemPrompt += "\n\n" + SkillHandler.GetContext();
            foreach (string injection in ExternalToolRegistry.GetContextInjections(enabledTools))
                systemPrompt += "\n\n" + injection;
        }

        JObject systemMsg = new JObject();
        systemMsg["role"] = "system";
        systemMsg["content"] = systemPrompt;
        messages.Add(systemMsg);

        // Process all user messages in the conversation list
        if (conversation != null)
        {
            foreach (var msg in conversation)
            {
                messages.Add(BuildMessageObject(msg));
            }
        }

        payload["messages"] = messages;

        if (includeTools)
        {
            // Add tools filtered by mode (Plan mode = read-only tools only)
            JArray toolsArray = ToolDefinitions.BuildToolsArray(modeId);
            payload["tools"] = toolsArray;
        }

        payload["stream"] = true;

        string reasoningEffort = ConfigHandler.GetReasoningEffort();
        if (!string.IsNullOrEmpty(reasoningEffort))
            payload["reasoning_effort"] = reasoningEffort;

        if (stopRequested != null && stopRequested())
        {
            return new LLMCompletionResponse("", new List<ToolHandler.ToolCall>(), "stopped");
        }

        return SendHttpRequest(payload, outputCallback, toolCallCallback, stopRequested, startBlock);
    }
}
}
