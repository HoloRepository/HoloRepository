import os
import secrets
from typing import Annotated

import jwt
from fastapi import Depends, HTTPException, status
from fastapi.security import OAuth2PasswordBearer
from pwdlib import PasswordHash

from models import User

oauth2_scheme = OAuth2PasswordBearer(tokenUrl="token")


def create_user(username: str, password: str) -> User: ...


def authenticate(username: str, password: str) -> User | None: ...


def create_access_token(user: User) -> str: ...


async def get_current_user(token: Annotated[str, Depends(oauth2_scheme)]) -> User: ...


CurrentUser = Annotated[User, Depends(get_current_user)]
