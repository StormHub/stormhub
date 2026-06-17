import { EventType, RunAgentInputSchema } from "@ag-ui/core";
import { EventEncoder } from "@ag-ui/encoder";
import express, { type Request, type Response } from "express";
import { runOrchestratorAgent } from "./agui/agent.js";

export function createServer() {
  const app = express();
  app.use(express.json());

  app.get("/health", (_req: Request, res: Response) => {
    res.json({ status: "ok" });
  });

  // AG-UI endpoint: accepts a RunAgentInput and streams protocol events as SSE.
  app.post("/", async (req: Request, res: Response) => {
    const parsed = RunAgentInputSchema.safeParse(req.body);
    if (!parsed.success) {
      res.status(400).json({ error: "Invalid RunAgentInput", issues: parsed.error.issues });
      return;
    }
    const input = parsed.data;

    const encoder = new EventEncoder();
    res.setHeader("Content-Type", encoder.getContentType());
    res.setHeader("Cache-Control", "no-cache");
    res.setHeader("Connection", "keep-alive");
    res.flushHeaders();

    try {
      for await (const event of runOrchestratorAgent(input)) {
        res.write(encoder.encode(event));
      }
    } catch (err) {
      // Orchestration failures already surface as RUN_ERROR from the agent;
      // this guards against transport-level errors mid-stream.
      const message = err instanceof Error ? err.message : "Unknown error";
      res.write(encoder.encode({ type: EventType.RUN_ERROR, message }));
    } finally {
      res.end();
    }
  });

  return app;
}
