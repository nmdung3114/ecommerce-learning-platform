from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from fastapi.responses import JSONResponse
from fastapi import Request, status
import os, logging

from app.config import settings
from app.core.middleware import ProcessTimeMiddleware, SecurityHeadersMiddleware
from app.core.exceptions import AppException
from app.routers import auth, users, products, cart, orders, learning, payment, admin, ai
from app.routers import wishlist, notifications, certificates, blog, instructor

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = FastAPI(
    title=settings.APP_NAME,
    version="1.0.0",
    docs_url="/api/docs" if settings.DEBUG else None,
    redoc_url="/api/redoc" if settings.DEBUG else None,
    openapi_url="/api/openapi.json" if settings.DEBUG else None,
)

# ── Middleware ─────────────────────────────────────────────
app.add_middleware(
    CORSMiddleware,
    allow_origins=settings.cors_origins_list,
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.add_middleware(ProcessTimeMiddleware)
app.add_middleware(SecurityHeadersMiddleware)

# ── Exception handlers ─────────────────────────────────────
@app.exception_handler(AppException)
async def app_exception_handler(request: Request, exc: AppException):
    return JSONResponse(status_code=exc.status_code, content={"detail": exc.detail})

@app.exception_handler(Exception)
async def generic_exception_handler(request: Request, exc: Exception):
    logger.error(f"Unhandled error: {exc}", exc_info=True)
    return JSONResponse(
        status_code=status.HTTP_500_INTERNAL_SERVER_ERROR,
        content={"detail": "Lỗi hệ thống, vui lòng thử lại sau"},
    )

# ── Routers ────────────────────────────────────────────────
app.include_router(auth.router)
app.include_router(users.router)
app.include_router(products.router)
app.include_router(cart.router)
app.include_router(orders.router)
app.include_router(learning.router)
app.include_router(payment.router)
app.include_router(admin.router)
app.include_router(ai.router)
app.include_router(wishlist.router)
app.include_router(notifications.router)
app.include_router(certificates.router)
app.include_router(blog.router)
app.include_router(instructor.router)

# ── Upload dir ─────────────────────────────────────────────
os.makedirs(settings.UPLOAD_DIR, exist_ok=True)
app.mount("/uploads", StaticFiles(directory=settings.UPLOAD_DIR), name="uploads")

# ── Health ─────────────────────────────────────────────────
@app.get("/api/health")
def health():
    return {"status": "ok", "app": settings.APP_NAME}

@app.on_event("startup")
async def startup():
    logger.info(f"🚀 {settings.APP_NAME} starting in {settings.APP_ENV} mode")
    # Auto-create new tables (idempotent)
    from app.database import engine, Base
    import app.models  # ensure all models are imported
    Base.metadata.create_all(bind=engine)
