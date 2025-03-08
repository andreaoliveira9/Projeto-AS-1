import json
import os
import random
import uuid
import logging

from locust import HttpUser, task, between, LoadTestShape
from locust_plugins.users.playwright import PlaywrightUser, pw, PageWithRetry, event

from opentelemetry import context, baggage, trace
from opentelemetry.metrics import set_meter_provider
from opentelemetry.sdk.metrics import MeterProvider
from opentelemetry.sdk.metrics.export import PeriodicExportingMetricReader
from opentelemetry.sdk.trace import TracerProvider
from opentelemetry.sdk.trace.export import BatchSpanProcessor
from opentelemetry.exporter.otlp.proto.grpc.metric_exporter import OTLPMetricExporter
from opentelemetry.exporter.otlp.proto.grpc.trace_exporter import OTLPSpanExporter
from opentelemetry.instrumentation.jinja2 import Jinja2Instrumentor
from opentelemetry.instrumentation.requests import RequestsInstrumentor
from opentelemetry.instrumentation.system_metrics import SystemMetricsInstrumentor
from opentelemetry.instrumentation.urllib3 import URLLib3Instrumentor
from opentelemetry._logs import set_logger_provider
from opentelemetry.exporter.otlp.proto.grpc._log_exporter import OTLPLogExporter
from opentelemetry.sdk._logs import LoggerProvider, LoggingHandler
from opentelemetry.sdk._logs.export import BatchLogRecordProcessor
from opentelemetry.sdk.resources import Resource

from openfeature import api
from openfeature.contrib.provider.flagd import FlagdProvider
from openfeature.contrib.hook.opentelemetry import TracingHook

from playwright.async_api import Route, Request

# Cria um logger para este módulo
logger = logging.getLogger(__name__)

# Configuração do Logger Provider do OpenTelemetry
logger_provider = LoggerProvider(
    resource=Resource.create({"service.name": "load-generator"})
)
set_logger_provider(logger_provider)

log_exporter = OTLPLogExporter(insecure=True)
logger_provider.add_log_record_processor(BatchLogRecordProcessor(log_exporter))
handler = LoggingHandler(level=logging.INFO, logger_provider=logger_provider)

# Anexa o handler OTLP ao logger raiz
logging.getLogger().addHandler(handler)
logging.getLogger().setLevel(logging.INFO)

metric_exporter = OTLPMetricExporter(insecure=True)
set_meter_provider(MeterProvider([PeriodicExportingMetricReader(metric_exporter)]))

tracer_provider = TracerProvider()
trace.set_tracer_provider(tracer_provider)
tracer_provider.add_span_processor(BatchSpanProcessor(OTLPSpanExporter()))

# Instrumentação manual para evitar problemas com o monkey patch do gevent do Locust
Jinja2Instrumentor().instrument()
RequestsInstrumentor().instrument()
SystemMetricsInstrumentor().instrument()
URLLib3Instrumentor().instrument()

API_URL = os.getenv("API_URL")


class OrderLoadTestUser(HttpUser):
    wait_time = between(1, 3)

    def __init__(self, *args, **kwargs):
        super().__init__(*args, **kwargs)
        self.user_id = f"test-user-{random.randint(1000, 9999)}"

    @task
    def send_order_request(self):
        if not API_URL:
            logger.error(
                "API_URL is not set. Set the environment variable or provide a default."
            )
            return

        request_id = str(uuid.uuid4())
        headers = {"x-requestid": request_id, "Content-Type": "application/json"}

        order_data = {
            "userId": self.user_id,
            "userName": "Test User",
            "city": "Seattle",
            "street": "123 Main St",
            "state": "WA",
            "country": "USA",
            "zipCode": "98101",
            "cardNumber": "4111111111111111",
            "cardHolderName": "Test User",
            "cardExpiration": "2028-01-01T00:00:00Z",
            "cardSecurityNumber": "123",
            "cardTypeId": 1,
            "buyer": "Test Buyer",
            "items": [
                {
                    "productId": 1,
                    "productName": "Test Product",
                    "unitPrice": 10.0,
                    "quantity": 2,
                }
            ],
        }

        try:
            response = self.client.post(
                f"{API_URL}/api/orders?api-version=1.0",  # URL absoluto (Locust usa o host fornecido)
                json=order_data,
                headers=headers,
            )
            if response.status_code == 200:
                logger.info(f"Request successful: {request_id}")
            else:
                logger.error(
                    f"Request failed with status {response.status_code}: {response.text}"
                )
        except Exception as e:
            logger.error(f"Request failed: {e}")


class ConstantUserLoad(LoadTestShape):
    """
    Este LoadTestShape define uma carga constante de 5 usuários com uma taxa de spawn de 1 usuário por segundo.
    """

    def tick(self, run_time=0):
        # Sempre retorna 5 usuários ativos e taxa de spawn 1.
        return (5, 1)
