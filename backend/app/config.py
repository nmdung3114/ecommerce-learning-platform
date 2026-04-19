from pydantic_settings import BaseSettings
from pydantic import field_validator
from typing import List
import os


class Settings(BaseSettings):
    # App
    APP_NAME: str = "ELearning Platform"
    APP_ENV: str = "development"
    DEBUG: bool = True
    SECRET_KEY: str = "change-me-in-production"

    # Database
    DATABASE_URL: str = "mysql+pymysql://elearning:elearning123@mysql:3306/elearning"

    # JWT
    JWT_SECRET_KEY: str = "jwt-secret-change-me"
    JWT_ALGORITHM: str = "HS256"
    JWT_ACCESS_TOKEN_EXPIRE_MINUTES: int = 1440  # 24h
    JWT_REFRESH_TOKEN_EXPIRE_DAYS: int = 30

    # VNPay
    VNPAY_TMN_CODE: str = ""
    VNPAY_HASH_SECRET: str = ""
    VNPAY_URL: str = "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html"
    VNPAY_RETURN_URL: str = "http://localhost/api/payment/vnpay-return"
    # URL công khai (vd. ngrok) để VNPay gọi IPN server-to-server; để trống thì không gửi vnp_IpnUrl
    VNPAY_IPN_URL: str = ""

    # PayPal Sandbox
    PAYPAL_CLIENT_ID: str = "ARoevRyCbe2QsWaAiz8Ld1ZkNVCgP8Yx5LzvRwfmXZjH1uHcpFyASAiRw_aiqxley4VJWjsKG62GpLLE"
    PAYPAL_CLIENT_SECRET: str = "ENsq-AKEERyTzga8Oo5kzmPNi313YuYmml9GqrkAuxAWGUkvQcZHp_PdJmoToEZvZN1mBlNbSQc_U0T5"
    PAYPAL_BASE_URL: str = "https://api-m.sandbox.paypal.com"
    PAYPAL_RETURN_URL: str = "http://localhost/api/payment/paypal-return"
    PAYPAL_CANCEL_URL: str = "http://localhost/checkout/index.html"
    PAYPAL_VND_RATE: float = 26000.0  # 1 USD = 26,000 VND (sandbox mock rate)


    # Mux
    MUX_TOKEN_ID: str = ""
    MUX_TOKEN_SECRET: str = ""
    MUX_SIGNING_KEY_ID: str = ""
    MUX_SIGNING_PRIVATE_KEY: str = ""

    # Gemini AI
    GEMINI_API_KEY: str = ""

    # CORS
    CORS_ORIGINS: str = "http://localhost,http://localhost:80,http://127.0.0.1"

    # File Upload
    UPLOAD_DIR: str = "/app/uploads"
    MAX_UPLOAD_SIZE_MB: int = 50

    @property
    def cors_origins_list(self) -> List[str]:
        return [o.strip() for o in self.CORS_ORIGINS.split(",") if o.strip()]

    class Config:
        env_file = ".env"
        case_sensitive = True
        extra = "ignore"


settings = Settings()
