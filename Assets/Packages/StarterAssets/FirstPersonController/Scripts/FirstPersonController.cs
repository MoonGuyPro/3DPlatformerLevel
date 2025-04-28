using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
#endif

namespace StarterAssets
{
	[RequireComponent(typeof(CharacterController))]
#if ENABLE_INPUT_SYSTEM
	[RequireComponent(typeof(PlayerInput))]
#endif
	public class FirstPersonController : MonoBehaviour
	{
		[Header("Player")]
		[Tooltip("Move speed of the character in m/s")]
		public float MoveSpeed = 4.0f;
		[Tooltip("Sprint speed of the character in m/s")]
		public float SprintSpeed = 6.0f;
		[Tooltip("Rotation speed of the character")]
		public float RotationSpeed = 1.0f;
		[Tooltip("Acceleration and deceleration")]
		public float SpeedChangeRate = 10.0f;

		[Space(10)]
		[Tooltip("The height the player can jump")]
		public float JumpHeight = 1.2f;
		[Tooltip("The character uses its own gravity value. The engine default is -9.81f")]
		public float Gravity = -15.0f;

		[Space(10)]
		[Tooltip("Time required to pass before being able to jump again. Set to 0f to instantly jump again")]
		public float JumpTimeout = 0.1f;
		[Tooltip("Time required to pass before entering the fall state. Useful for walking down stairs")]
		public float FallTimeout = 0.15f;

		[Header("Player Grounded")]
		[Tooltip("If the character is grounded or not. Not part of the CharacterController built in grounded check")]
		public bool Grounded = true;
		[Tooltip("Useful for rough ground")]
		public float GroundedOffset = -0.14f;
		[Tooltip("The radius of the grounded check. Should match the radius of the CharacterController")]
		public float GroundedRadius = 0.5f;
		[Tooltip("What layers the character uses as ground")]
		public LayerMask GroundLayers;

		[Header("Cinemachine")]
		[Tooltip("The follow target set in the Cinemachine Virtual Camera that the camera will follow")]
		public GameObject CinemachineCameraTarget;
		[Tooltip("How far in degrees can you move the camera up")]
		public float TopClamp = 90.0f;
		[Tooltip("How far in degrees can you move the camera down")]
		public float BottomClamp = -90.0f;

		// cinemachine
		private float _cinemachineTargetPitch;

		// player
		private float _speed;
		private float _rotationVelocity;
		private float _verticalVelocity;
		private float _terminalVelocity = 53.0f;

		// timeout deltatime
		private float _jumpTimeoutDelta;
		private float _fallTimeoutDelta;

        [Header("Wall Running")]
        public LayerMask WallRunLayer;
        public float WallRunSpeed = 6f;
        public float WallRunDuration = 1.5f;
        public float WallRunGravity = -2f;
        public float WallCheckDistance = 0.7f;
        public float MinJumpHeight = 1.2f;

        private bool _isWallRunning;
        private float _wallRunTimer;
        private Vector3 _wallRunDirection;

        [Header("Dash Settings")]
        public float DashSpeed = 20f;
        public float DashDuration = 0.2f;
        public float DashCooldown = 2f;

        private bool _isDashing = false;
        private float _dashTimeRemaining;
        private float _dashCooldownRemaining;
        private Vector3 _dashDirection;






#if ENABLE_INPUT_SYSTEM
        private PlayerInput _playerInput;
#endif
		private CharacterController _controller;
		private StarterAssetsInputs _input;
		private GameObject _mainCamera;

		private const float _threshold = 0.01f;

		private bool IsCurrentDeviceMouse
		{
			get
			{
				#if ENABLE_INPUT_SYSTEM
				return _playerInput.currentControlScheme == "KeyboardMouse";
				#else
				return false;
				#endif
			}
		}

		private void Awake()
		{
			// get a reference to our main camera
			if (_mainCamera == null)
			{
				_mainCamera = GameObject.FindGameObjectWithTag("MainCamera");
			}
		}

		private void Start()
		{
			_controller = GetComponent<CharacterController>();
			_input = GetComponent<StarterAssetsInputs>();
#if ENABLE_INPUT_SYSTEM
			_playerInput = GetComponent<PlayerInput>();
#else
			Debug.LogError( "Starter Assets package is missing dependencies. Please use Tools/Starter Assets/Reinstall Dependencies to fix it");
#endif

			// reset our timeouts on start
			_jumpTimeoutDelta = JumpTimeout;
			_fallTimeoutDelta = FallTimeout;
		}

		private void Update()
		{
            GroundedCheck();
            HandleDashInput();
            WallRunUpdate();
            JumpAndGravity();
            Move();
            HandleLevelReset();
        }

		private void LateUpdate()
		{
			CameraRotation();
		}

		private void GroundedCheck()
		{
			// set sphere position, with offset
			Vector3 spherePosition = new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z);
			Grounded = Physics.CheckSphere(spherePosition, GroundedRadius, GroundLayers, QueryTriggerInteraction.Ignore);
		}

		private void CameraRotation()
		{
			// if there is an input
			if (_input.look.sqrMagnitude >= _threshold)
			{
				//Don't multiply mouse input by Time.deltaTime
				float deltaTimeMultiplier = IsCurrentDeviceMouse ? 1.0f : Time.deltaTime;
				
				_cinemachineTargetPitch += _input.look.y * RotationSpeed * deltaTimeMultiplier;
				_rotationVelocity = _input.look.x * RotationSpeed * deltaTimeMultiplier;

				// clamp our pitch rotation
				_cinemachineTargetPitch = ClampAngle(_cinemachineTargetPitch, BottomClamp, TopClamp);

				// Update Cinemachine camera target pitch
				CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(_cinemachineTargetPitch, 0.0f, 0.0f);

				// rotate the player left and right
				transform.Rotate(Vector3.up * _rotationVelocity);
			}
		}

        private void Move()
        {
            if (_isWallRunning)
            {
                // Jeśli gracz jest na ścianie, to porusza się wzdłuż niej
                Vector3 wallRunMovement = _wallRunDirection * _speed * Time.deltaTime;

                // Jeśli gracz wciśnie przycisk skoku, może spróbować wyskoczyć w górę
                if (_input.jump)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                }

                // Wykonaj ruch wzdłuż ściany
                _controller.Move(wallRunMovement);

                // W przypadku przytrzymywania ruchu w dół na ścianie, zmień kierunek biegu
                if (_input.move.y < 0)
                {
                    _controller.Move(-wallRunMovement); // Możesz dostosować to do gry, by gracz mógł także "zjeżdżać"
                }
                return;
            }

            // Jeśli gracz nie jest na ścianie, wykonaj tradycyjny ruch
            float targetSpeed = _input.sprint ? SprintSpeed : MoveSpeed;

            if (_input.move == Vector2.zero) targetSpeed = 0.0f;

            float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0.0f, _controller.velocity.z).magnitude;

            float speedOffset = 0.1f;
            float inputMagnitude = _input.analogMovement ? _input.move.magnitude : 1f;

            // Przyspieszanie/decelaracja
            if (currentHorizontalSpeed < targetSpeed - speedOffset || currentHorizontalSpeed > targetSpeed + speedOffset)
            {
                _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed * inputMagnitude, Time.deltaTime * SpeedChangeRate);
                _speed = Mathf.Round(_speed * 1000f) / 1000f;
            }
            else
            {
                _speed = targetSpeed;
            }

            // Normalizowanie kierunku ruchu
            Vector3 inputDirection = new Vector3(_input.move.x, 0.0f, _input.move.y).normalized;

            // Ruch gracza
            if (_input.move != Vector2.zero)
            {
                inputDirection = transform.right * _input.move.x + transform.forward * _input.move.y;
            }

            // Wykonaj normalny ruch
            _controller.Move(inputDirection.normalized * (_speed * Time.deltaTime) + new Vector3(0.0f, _verticalVelocity, 0.0f) * Time.deltaTime);
        }


        private void JumpAndGravity()
        {
            if (Grounded)
            {
                _fallTimeoutDelta = FallTimeout;

                if (_verticalVelocity < 0.0f)
                {
                    _verticalVelocity = -2f;
                }

                if (_input.jump && _jumpTimeoutDelta <= 0.0f)
                {
                    _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);
                }

                if (_jumpTimeoutDelta >= 0.0f)
                {
                    _jumpTimeoutDelta -= Time.deltaTime;
                }
            }
            else
            {
                _jumpTimeoutDelta = JumpTimeout;

                if (_fallTimeoutDelta >= 0.0f)
                {
                    _fallTimeoutDelta -= Time.deltaTime;
                }

                if (!_isWallRunning)
                {
                    _input.jump = false;
                }
            }

            if (!_isWallRunning)
            {
                if (_verticalVelocity < _terminalVelocity)
                {
                    _verticalVelocity += Gravity * Time.deltaTime;
                }
            }
            else
            {
                _verticalVelocity = WallRunGravity;
            }
        }



        private static float ClampAngle(float lfAngle, float lfMin, float lfMax)
		{
			if (lfAngle < -360f) lfAngle += 360f;
			if (lfAngle > 360f) lfAngle -= 360f;
			return Mathf.Clamp(lfAngle, lfMin, lfMax);
		}

		private void OnDrawGizmosSelected()
		{
			Color transparentGreen = new Color(0.0f, 1.0f, 0.0f, 0.35f);
			Color transparentRed = new Color(1.0f, 0.0f, 0.0f, 0.35f);

			if (Grounded) Gizmos.color = transparentGreen;
			else Gizmos.color = transparentRed;

			// when selected, draw a gizmo in the position of, and matching radius of, the grounded collider
			Gizmos.DrawSphere(new Vector3(transform.position.x, transform.position.y - GroundedOffset, transform.position.z), GroundedRadius);
		}

        private void WallRunUpdate()
        {
            if (_isWallRunning)
            {
                // Jeśli nie wykrywa już ściany — koniec wallruna
                if (!IsWallDetected(out RaycastHit wallHitCheck, out Vector3 wallNormalCheck))
                {
                    EndWallRun();
                    return;
                }

                // Skok w trakcie wallruna
                if (_input.jump)
                {
                    PerformWallRunJump();
                    return;
                }

                // Koniec wallruna jeśli gracz przestaje się poruszać lub czas minął
                if (_input.move.y <= 0.1f || _wallRunTimer <= 0f)
                {
                    EndWallRun();
                    return;
                }

                _wallRunTimer -= Time.deltaTime;

                // Poruszamy gracza wzdłuż kierunku biegu po ścianie
                _controller.Move(_wallRunDirection * WallRunSpeed * Time.deltaTime);
                return;
            }


            if (Grounded || _verticalVelocity > 0) return;
            if (_input.move.y <= 0.1f) return;

            // WYKRYWANIE ŚCIANY
            if (IsWallDetected(out RaycastHit hit, out Vector3 wallNormal))
            {
                StartWallRun(wallNormal);
            }
        }




        private Vector3 _wallNormal;

        private void StartWallRun(Vector3 wallNormal)
        {
            _isWallRunning = true;
            _wallRunTimer = WallRunDuration;
            _verticalVelocity = 0f;

            _wallNormal = wallNormal;

            _wallRunDirection = Vector3.Cross(wallNormal, Vector3.up);
            if (Vector3.Dot(_wallRunDirection, transform.forward) < 0)
                _wallRunDirection = -_wallRunDirection;
        }

        private void EndWallRun()
        {
            _isWallRunning = false;
            _wallRunTimer = 0f;
            _verticalVelocity = 0f; // natychmiastowe spadanie
        }


        private bool IsWallDetected(out RaycastHit wallHit, out Vector3 wallNormal)
        {
            // Sprawdzamy po prawej
            if (Physics.Raycast(transform.position, transform.right, out wallHit, WallCheckDistance, WallRunLayer))
            {
                wallNormal = wallHit.normal;
                return true;
            }
            // Sprawdzamy po lewej
            if (Physics.Raycast(transform.position, -transform.right, out wallHit, WallCheckDistance, WallRunLayer))
            {
                wallNormal = wallHit.normal;
                return true;
            }

            wallNormal = Vector3.zero;
            return false;
        }


        private void PerformWallRunJump()
        {
            EndWallRun(); // <- zakończ wallrun NATYCHMIAST

            // kierunek skoku: głównie w górę, lekko od ściany
            Vector3 jumpDirection = (Vector3.up + _wallNormal * 0.5f).normalized;

            // ustaw pionową prędkość jak przy normalnym skoku
            _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

            // dodaj impuls poziomy – boost od ściany
            _controller.Move(jumpDirection * 5f * Time.deltaTime);

            // zabezpieczenie, żeby nie dało się od razu znowu przykleić
            _fallTimeoutDelta = 0f;
            _jumpTimeoutDelta = JumpTimeout;
        }

        private void HandleDashInput()
        {
            // Odliczanie cooldownu
            if (_dashCooldownRemaining > 0f)
                _dashCooldownRemaining -= Time.deltaTime;

            // Trwa dash
            if (_isDashing)
            {
                _dashTimeRemaining -= Time.deltaTime;
                _controller.Move(_dashDirection * DashSpeed * Time.deltaTime);

                if (_dashTimeRemaining <= 0f)
                {
                    _isDashing = false;
                }

                return; // Blokuje ruch z Move() podczas dasza
            }

            // Jeśli LPM wciśnięty i cooldown się skończył
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current.leftButton.wasPressedThisFrame && _dashCooldownRemaining <= 0f)
#else
    if (Input.GetMouseButtonDown(0) && _dashCooldownRemaining <= 0f)
#endif
            {
                _isDashing = true;
                _dashTimeRemaining = DashDuration;
                _dashCooldownRemaining = DashCooldown;

                // Kierunek patrzenia (płaszczyzna pozioma)
                Vector3 lookDirection = _mainCamera.transform.forward;
                lookDirection.y = 0f;
                _dashDirection = lookDirection.normalized;
            }
        }

        private void HandleLevelReset()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            }
#if !ENABLE_INPUT_SYSTEM
    if (Input.GetKeyDown(KeyCode.R))
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
#endif
        }



    }


}