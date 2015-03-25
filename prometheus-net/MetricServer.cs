﻿using System;
using System.Diagnostics;
using System.Net;
using System.Reactive.Concurrency;
using Prometheus.Internal;

namespace Prometheus
{
    public class MetricServer
    {
        private const string PROTO_HEADER = "application/vnd.google.protobuf; proto=io.prometheus.client.MetricFamily; encoding=delimited";
        private readonly HttpListener _httpListener = new HttpListener();
        private static readonly string ProtoHeaderNoSpace = PROTO_HEADER.Replace(" ", "");


        public MetricServer(int port)
        {
            _httpListener.Prefixes.Add(string.Format("http://+:{0}/metrics/", port));
        }

        public void Start(IScheduler scheduler = null)
        {
            _httpListener.Start();

            StartLoop(scheduler ?? Scheduler.Default);
        }

        private static void ProcessScrapeRequest(HttpListenerContext context)
        {
            var response = context.Response;
            response.StatusCode = 200;

            const string text = "text/plain; version=0.0.4";

            string type = PROTO_HEADER;
            
            if (!context.Request.Headers.Get("Accept").Replace(" ", "").Contains(ProtoHeaderNoSpace))
            {
                type = text;
            }

            response.AddHeader("Content-Type", type);

            var collected = MetricsRegistry.Instance.CollectAll();
            using (var outputStream = response.OutputStream)
            {
                if (type == text)
                {
                    AsciiFormatter.Format(outputStream, collected);
                }
                else
                {
                    ProtoFormatter.Format(outputStream, collected);
                }
            }
            response.Close();
        }

        private void StartLoop(IScheduler scheduler)
        {
            scheduler.Schedule(_httpListener, (listener, action) => listener.BeginGetContext(ar =>
            {
                var t = (Tuple<HttpListener, Action<HttpListener>>)ar.AsyncState;
                var listInner = t.Item1;
                var httpListenerContext = listInner.EndGetContext(ar);
                try
                {
                    ProcessScrapeRequest(httpListenerContext);
                }
                catch (Exception e)
                {
                    Trace.WriteLine(string.Format("Error: {0}", e));
                }
                t.Item2(t.Item1);
            }, Tuple.Create(listener, action)));
        }

        public void Stop()
        {
            _httpListener.Stop();
            _httpListener.Close();
        }
    }
}