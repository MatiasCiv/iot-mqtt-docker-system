FROM python:3.11-slim

WORKDIR /app

# Nota: Eliminamos las dependencias de bluetooth nativas del contenedor 
# ya que la gestión del HC-05 la hace el host mediante `rfcomm-auto.service`.
# Solo dejamos dependencias esenciales si tuvieras alguna compilación, 
# si no, puedes dejar este bloque vacío.
RUN apt-get update && apt-get install -y \
    gcc \
    && rm -rf /var/lib/apt/lists/*

# Copiar requirements primero (optimización de caché)
COPY requirements.txt .

# Instalar dependencias Python
RUN pip install --no-cache-dir -r requirements.txt

# Copiar el resto del código
COPY . .

# 🔥 CRÍTICO: El flag "-u" (unbuffered) asegura logs en tiempo real.
# Al usar la sintaxis de corchetes [], garantizamos que Python sea el PID 1 
# y responda inmediatamente al SIGTERM de Docker, eliminando el Exit 137.
CMD ["python", "-u", "main.py"]